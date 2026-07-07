# Battle Event Trigger Architecture

This document describes the tactical runtime architecture for event-driven battle reactions as implemented.

The goal is to support actions that unfold over multiple committed steps while allowing battle systems to observe those steps and request follow-up work. Examples include overwatch fire, proximity mines, visibility changes, falling, morale checks, and other effects that happen between a unit's declared action and that action's final destination.

## Goals

- Keep `BattleSession` as the authoritative source of tactical state.
- Keep battle state changes single-threaded and deterministic.
- Treat movement, firing, and item use as actions that may unfold over several committed steps.
- Allow battle observers to inspect committed battle events such as `TileOccupied` after the related primitive action commits.
- Make observer ordering explicit instead of depending on raw C# event subscription order.
- Allow interrupts to pause, cancel, or resume a longer action.
- Keep presentation events separate from authoritative rule resolution.

## Starting Event Set

The initial event vocabulary should stay small:

- `UnitMoved`
- `TileOccupied`
- `UnitDamaged`
- `UnitKilled`
- `TurnStarted`
- `TurnEnded`

These event types describe committed state changes. Consequences are resolved from events as they are raised, mediated by the session's hook registry.

## Initial Implementation

The first implementation slice now exists in code:

- `TileOccupiedBattleEvent` is emitted after successful movement steps.
- `MoveUnitStep` publishes `UnitMoved` and `TileOccupied` after a successful move.
- `ApplyDamage` publishes `UnitDamaged` and lethal `UnitKilled`.
- `EndFactionTurn` publishes `TurnEnded` and `TurnStarted` transition events.
- `BattleHook` and the session's internal `BattleHookRegistry` provide mediated, deterministic hook evaluation bucketed by `BattleEventTag` type, firing in a `Before` or `After` phase relative to each event's public broadcast.
- `BattleAction` and `MoveUnit` give the executor explicit high-level queued work and primitive action commits.
- `BattleActionExecutor` accepts a submitted `BattleAction`, commits primitive actions one at a time inside an action window, and inserts any interrupt actions hooks returned during that primitive's event dispatch ahead of paused work once the window closes.

Current limitations:

- Concrete overwatch and proximity mine rules are not implemented yet; tests use simple damage hooks as proof-of-concept interrupt responses.
- Runtime interrupt responses require a persistent `BattleActionExecutor` when a composite action needs to pause and resume around them.
- The old predictive command surfaces have been removed. Queued player/AI intent must be expressed as `BattleAction` values such as `MoveUnit`, and primitive actions are the commit layer.
- The session's hook registry indexes hooks by `BattleEventTag` type (concrete event records and the marker interfaces they implement) and `HookPhase`. It does not yet index by tile, faction, owner, or source item.

## Core Runtime Shape

```text
Controller / AI
  -> BattleAction
  -> BattleActionExecutor.Submit(action)
  -> inspect the action at the head of the queue
  -> ask the head action for its next primitive action
  -> open an action window for that primitive's Execute
  -> validate and commit the primitive action; each event it raises fires
     Before hooks, broadcasts BattleEventCommitted, then fires After hooks
  -> close the action window and collect any interrupt actions hooks
     returned, in evaluation order
  -> continue until the submitted action and its interrupts settle
  -> return the ordered action result list
```

The important distinction is between composite actions and primitive actions.

- A high-level action is intent, such as "move this unit through these destination tiles."
- A primitive action is an atomic authoritative state change, such as "move this unit from tile A to adjacent tile B."

Longer actions should be modeled as sequences of primitive actions. This gives the runtime a stable checkpoint after each step where visibility, hooks, interrupts, and presentation updates can happen.

The executor should be understood as an action queue processor. The queue head is the currently executing or about-to-execute action. Interrupt actions are also actions. When events raised by the head action's commit match registered hooks and those hooks return interrupt actions, the executor enqueues them once the primitive's action window closes, so they run before the interrupted head action continues. Those interrupt actions can commit events of their own, which may enqueue more actions.

## Event Dispatch and Hook Timing

Battle observers are consulted as each event is raised, not only after the whole primitive settles.

The primitive action is responsible for raising the domain events caused by its authoritative state change. As each event is raised, the session's dispatch loop fires that event's `Before` hooks, broadcasts `BattleEventCommitted`, then fires its `After` hooks. When this firing happens inside an executor action window, any interrupt actions the hooks return accumulate into that primitive's interrupt buffer.

For movement, this means:

```text
validate MoveUnitStep
  -> commit the unit onto the destination tile
  -> raise UnitMoved: Before hooks fire -> broadcast -> After hooks fire
  -> raise TileOccupied: Before hooks fire -> broadcast -> After hooks fire
  -> executor closes the action window and collects any interrupts those
     hooks returned
  -> run planned interrupt while the unit is on that tile
  -> continue, interrupt, or cancel the remaining movement
```

This matters for overwatch and mines. If a unit steps from `X` to `X + 1`, the hook should evaluate against the post-step state where the unit occupies `X + 1`. The longer move to `X + N` is still unresolved and can be paused or cancelled.

Rules that prevent a commit, such as "this tile cannot be entered," should remain validation rules. Rules that react to an occurred fact, such as "a watched tile became occupied," should be hook rules — registered `After`, since they want to observe a settled fact. A `Before` hook is for the rarer case where a hook's own mutation should be visible to that same event's broadcast observers (buffs are the current example). Either way, interrupt responses run inside the primitive's dispatch, so overwatch/mine interrupts still act on the committed state that caused the event.

For overwatch, the hook is discovered from the raised movement event, so the target is physically on the watched tile when the shot executes. For a proximity mine, detonation likewise happens after the unit has entered the triggering tile.

## Proposed Components

### BattleAction

`BattleAction` represents work requested by a player, AI, hook interrupt, or system.

Examples:

- `MoveUnit`
- `ReactionFire`
- `DetonateMine`
- `ApplyDamage`
- `EndFactionTurn`

An action may produce one or more primitive actions over time. `MoveUnit` produces one `MoveUnitStep` at a time. `ReactionFire` may produce a turn-to-target step, a shot animation step, and an `ApplyDamage` action.

### BattleActionExecutor

`BattleActionExecutor` owns action ordering and interruption.

Responsibilities:

- accept caller intent through `Submit`
- keep an ordered action queue
- treat the queue head as the active or about-to-execute action
- open an action window around each primitive's execution (`BeginActionExecution`/`EndActionExecution`) so hooks firing during its event dispatch have somewhere to deposit interrupt actions
- insert interrupt actions ahead of a paused action once the window closes, aggregated across the whole primitive and reversed once
- discard a window's interrupts entirely if the primitive fails
- decide whether the active action continues, pauses, cancels, or completes
- return the ordered public result list from the submission

The executor should still be single-threaded. It is a scheduler and resolver, not a parallel event bus.

A useful mental model:

```text
queue: [MoveUnit]

MoveUnit step raises TileOccupied
  -> an After hook (overwatch) returns ReactionFire
  -> the action window closes; executor inserts ReactionFire before the
     remaining MoveUnit work

queue while resolving: [ReactionFire, MoveUnit]

ReactionFire may raise UnitDamaged
  -> a damage hook may insert another interrupt
```

The queue can therefore grow while a submission is being resolved. The executor controls where new actions are inserted so nested interrupts remain deterministic, and a normal submission leaves the queue empty when it returns.

When a single primitive raises more than one event (for example a move raising both `UnitMoved` and `TileOccupied`), hooks fire per event — Before, broadcast, After — in raise order, and their returned interrupt actions accumulate in that order into the primitive's action window. The executor reverses the combined list once before inserting it — never per event — so interrupts still insert in evaluation order (event 1's ahead of event 2's) at the front of the queue.

The public result list does not include primitive child actions produced by a composite action. A `MoveUnit` that commits multiple `MoveUnitStep` actions hides those step results from callers. The committed `BattleEvent` stream remains the source for per-step movement and hook details.

The executor may publish presentation notifications after rule resolution, but it should not be the source of truth for domain events. Primitive actions and the session emit committed `BattleEvent` values as they change authoritative state.

### Primitive BattleAction

Primitive `BattleAction` types are the commit layer.

Primitive actions own their state changes and event emission because they know exactly what changed. For example, `MoveUnitStep` knows the source tile, destination tile, and moved unit after it changes board occupancy. `ApplyDamage` knows the affected unit, damage amount, and whether the damage reduced health to zero.

The executor should not infer events by diffing session state. It opens an action window around a primitive's execution so the session's dispatch loop can attribute the hook firings and interrupt actions that happen during that primitive to it, rather than capturing and diffing committed state itself.

Good primitive actions:

- `MoveUnitStep`
- `ApplyDamage`
- `KillUnit`
- `StartBattle`
- `EndFactionTurn`

High-level movement should be represented by composite actions such as `MoveUnit`. The primitive movement commit should remain `MoveUnitStep`.

### BattleEvent

`BattleEvent` describes a committed battle fact. Hook registration keys on `BattleEventTag` types — the concrete event record type plus any `BattleEventTag` marker interfaces it implements (such as `IPositionedBattleEvent`) — while concrete event subclasses carry only the payload that applies to that event.

Events need enough context for hooks to evaluate without guessing:

```text
UnitMovedBattleEvent
  UnitId
  Position
  SourcePosition

TileOccupiedBattleEvent
  UnitId
  Position
  SourcePosition

UnitDamagedBattleEvent
  UnitId
  Bundle (list of Damage packets)
  TotalAmount
  ArmorDamage
  HealthDamage

UnitKilledBattleEvent
  UnitId
  Position

TurnStartedBattleEvent / TurnEndedBattleEvent
  Faction
  TurnNumber
```

Shared interfaces such as `IPositionedBattleEvent` let hooks match common payload concepts without forcing every event into one large optional-field record.

### BattleHook

`BattleHook` (and the generic `BattleHook<TEvent>`) is the one battle observer/reactor concept — there is no phase-by-subclass hierarchy. A hook is registered against an event-tag type with a `HookPhase` (`Before` or `After`) and an int priority; it fires once per matching event, in that phase, receiving a `HookContext` (`Session`, `Phase`, `SourceAction: Option<BattleAction>` — the executor's in-flight primitive when one is open, else `None`).

```csharp
public abstract class BattleHook
{
  public abstract IReadOnlyList<BattleAction> OnEvent(HookContext context, BattleEvent battleEvent);
}
```

Examples:

- a proximity mine registered against affected tiles (`After` — reacts to a settled fact)
- an overwatch cone registered against watched tiles (`After`)
- session-internal bookkeeping such as `StatusEffectSystem` / `ArmorRegenSystem` / `ObjectiveSystem` (`After`, mutating state directly)
- buff evaluation (`Before` — its mutation must land before the turn-start/spawn event it rides broadcasts, and before the AP refresh that follows)

A hook may mutate `BattleSession` directly, raise follow-up events through `context.Session.RaiseEvent(...)` (queued, so the committed stream stays linear), and/or RETURN interrupt actions from `OnEvent`. Returned interrupts only mean something while an executor action is in flight: the session throws `InvalidOperationException` if a hook returns one outside that window — interrupts only mean something inside an executor action. Most hooks return `[]`.

One-shot hooks (mines) call `session.UnregisterHook<TEventKey>(this, phase)` on themselves from inside `OnEvent`; matches are materialized before a firing's iteration, so self-unregistration mid-loop is safe and takes effect on the next dispatch.

### Hook Registration

The session owns registered hooks and provides lookup through its internal `BattleHookRegistry`. Registration lives on the session; the public door is `BattleRuntime.RegisterHook`, choosing the event-tag bucket through a type parameter, the firing phase, and a priority given at registration:

```csharp
runtime.RegisterHook<TileOccupiedBattleEvent>(hook, HookPhase.After, priority: 0);
runtime.RegisterHook<IPositionedBattleEvent>(hook, HookPhase.Before); // fires for every positioned event
```

This is a deliberate scope change from an executor-owned registry: because hooks are registered on the *session* rather than on any one executor, a hook registered through this door fires for every dispatch on that session — including setup and turn-transition dispatches that no executor action produced — not just events an executor happens to be resolving, and the registration survives disposal of any one `BattleRuntime` wrapping it. `ThrowIfDisposed` still guards the `BattleRuntime.RegisterHook`/`UnregisterHook` doors themselves, so a disposed runtime cannot register or unregister hooks, but hooks already registered keep firing for the session's other executors.

Ordering within one (event, phase) bucket is int priority (lower first), then registration order (a monotonic stamp) — one vocabulary, no bucket enum.

The registry currently indexes by:

- event tag type (the concrete event record type plus the `BattleEventTag` marker interfaces it implements)
- phase (`Before` / `After`)

Later indices can include:

- tile
- owning unit
- source item
- faction

This supports the current runtime without broadcasting every event to every hook, while leaving room for spatial indexing if tile-based hook counts grow.

## Phases

There is one hook base class (`BattleHook`) and one registration door; `HookPhase` is registration data — a firing-order parameter — not a type hierarchy.

**`Before`** fires as an event is about to land — before the public `BattleEventCommitted` broadcast. A Before hook's mutations are visible to every broadcast observer of that event: "the event lands fully formed." Buff evaluation (`TurnStartBuffHook` on `TurnStartedBattleEvent`, `UnitSpawnedBuffHook` on `UnitAddedBattleEvent`) is registered `Before` so a buff flip — and any `MaxActionPoints` change it causes — lands before that event's own broadcast, and, for turn start, before the AP refresh that runs once the whole turn-start dispatch completes.

**`After`** fires once the event is history — the old listener window. Session-internal bookkeeping (`StatusEffectSystem`, `ArmorRegenSystem`, `ObjectiveSystem`, `CapabilityEffectSystem`) and gameplay interrupts (future overwatch/mines) both register `After`: bookkeeping because it wants to observe a fact that already happened, interrupts because they should act on settled state.

Both phases can mutate the session directly, raise follow-up events, and/or return interrupt actions from `OnEvent` — capability is not gated by phase. Ordering within a (event, phase) bucket is int priority (lower first) then registration order.

Interrupts (returned `BattleAction`s) only make sense while an executor action is executing a primitive: the executor opens an action window around each primitive's `Execute` (`BeginActionExecution`/`EndActionExecution`), and every hook that fires during that primitive's dispatch — Before or After, for every event the primitive raises — has its returned interrupts accumulate into that one window, in evaluation order. A hook returning interrupts with no window open (setup/turn-transition dispatches, spawns) is a bug surfaced loudly: the session throws. A failed primitive discards its window's interrupts entirely — a failed action enqueues no responses.

Non-phases — deliberately not part of the hook substrate:

- **Pre-broadcast visibility refresh** stays hardwired inside the session's dispatch loop, before `Before` hooks fire. Its fixed position is the mid-move guarantee overwatch and mines depend on, so it is not a registrable phase.
- **`OnActionStart`/`OnActionComplete`** stay notification-only executor events for presentation; they carry no write power and are not part of the hook substrate.

Documented upgrade paths, not built: a pre-execute veto phase, reactions to failed or rejected primitives, and cancellation of a paused composite's remaining steps. If cancellation is ever added, it should be one explicit field on the interrupt contract — never a four-way enum.

The rule of thumb: register `After` for a hook that should observe a settled fact (gameplay interrupts like overwatch/mines, or bookkeeping like armor regen and objectives); register `Before` when the hook's mutation must be visible to that same event's own broadcast (buffs, so a `MaxActionPoints` change lands before `TurnStarted`/`UnitAdded` is observed and before the AP refresh that follows).

`ArmorRegenSystem` is registered `After` on `TurnEndedBattleEvent` at priority `-100`. It ticks the regen delay and current armor for every living unit on that faction whose armor can regenerate, and raises `UnitArmorRegeneratedBattleEvent` only when armor is actually restored.

## Observer Rule

Observers should be event-driven, but mediated.

Prefer this:

```text
event raised -> session's hook registry finds matching hooks for this phase -> executor queues any interrupt actions they returned
```

Avoid this as the authoritative rule mechanism:

```text
event emitted -> arbitrary C# event subscribers mutate battle state
```

The rule is structural rather than a convention to remember: `BattleActionExecutor.Submit` throws if called while the session is dispatching events (`IsDispatchingEvents`), so no hook — of either phase — can smuggle in a second execution loop. The only write channel out of a hook toward the executor is the interrupt actions it returns from `OnEvent`, which the executor validates and applies through the normal action path once the current primitive's action window closes.

The mediated model keeps event-driven behavior while preserving deterministic ordering, save/load friendliness, and debuggability.

## Movement And Overwatch Flow

Example: a unit moves from tile `X` toward tile `X + N`, and an enemy has overwatch over an intermediate tile.

```text
1. Player or AI enqueues MoveUnit(unit, destinations: X + 1 -> ... -> X + N), using the unit's current session position as the route source.
2. Executor starts the move action and opens an action window for MoveUnitStep(unit, X, X + 1).
3. Executor validates and commits MoveUnitStep(unit, X, X + 1).
4. Session updates board occupancy and unit position.
5. MoveUnitStep raises UnitMoved: Before hooks fire, broadcast, After hooks fire.
6. MoveUnitStep raises TileOccupied for X + 1: Before hooks fire, broadcast, After hooks fire.
7. Overwatch's After hook on TileOccupied verifies the committed target is valid.
8. The hook returns [ReactionFire(observer, target)]; the interrupt lands in MoveUnitStep's action window.
9. The window closes once MoveUnitStep's Execute returns; the executor pauses MoveUnit and inserts ReactionFire at the head of the queue.
10. ReactionFire runs while the target is on X + 1.
11. ReactionFire applies damage if the shot hits.
12. Damage events may trigger more interrupts.
13. If the moving unit dies, the paused move action is cancelled.
14. If the moving unit survives, the move action resumes with the next step.
```

The target is therefore shot while occupying the hook's tile, not after the whole path has completed.

## Proximity Mine Flow

Example: a proximity mine is placed and listens for occupation of nearby tiles.

```text
1. Mine is placed.
2. Session registers a proximity-mine BattleHook (After) against the affected tiles' event key.
3. Unit attempts to move onto an affected tile.
4. MoveUnitStep commits and raises TileOccupied for that tile, inside its action window.
5. The mine's After hook fires for the raised event.
6. Mine hook verifies it is armed and the occupying unit is a valid target.
7. The hook calls session.UnregisterHook<TileOccupiedBattleEvent>(this, HookPhase.After) on itself, then returns [DetonateMine].
8. The interrupt lands in MoveUnitStep's action window; once the window closes the executor pauses movement and inserts DetonateMine at the head of the queue.
9. DetonateMine runs before the movement action continues.
10. DetonateMine publishes UnitDamaged or UnitKilled events.
11. Mine damage may trigger more interrupts.
12. If the mover survives and movement is still valid, the original action continues.
```

The mine itself is registered battle state, not a scene object. Presentation can subscribe to the same committed events to animate the detonation.

## Visibility Flow

Visibility should be updated at primitive checkpoints, especially after movement.

```text
MoveUnitStep commits
  -> visibility is refreshed, before that step's events fire any hooks
  -> UnitMoved / TileOccupied each fire Before hooks, broadcast, After hooks
  -> hooks that depend on post-step state resolve against the refreshed visibility
  -> newly visible or newly hidden units can emit visibility events later
```

The starting event list does not include visibility events yet, but the model can add them later:

- `UnitSpotted`
- `UnitLostFromSight`
- `TileRevealed`

For now, overwatch can use the raised event to discover that a watched tile became occupied, then validate against current per-unit visibility before the reaction shot executes.

## Action Continuation Rules

Each interrupt should resolve to a clear continuation decision:

- continue active action
- pause active action
- cancel active action
- replace active action
- end active unit activation

For movement:

- if the mover survives reaction fire, continue from the mover's current tile
- if the mover dies, cancel the remaining route
- if the destination becomes blocked, cancel or revalidate the remaining route
- if the mover loses action points, revalidate before each next step

The route should not be considered guaranteed after the action starts. It is a plan that is revalidated step by step.

## Ordering

The executor owns where interrupt actions are inserted into the queue; hook firing order itself is owned by the session's dispatch loop (priority, then registration order, within Before then After, per event). Initial ordering can be simple and explicit:

1. ask the active action for its next primitive action
2. open an action window, then validate and commit the primitive action
3. for each event the primitive raises: refresh derived state such as visibility, fire Before hooks, broadcast, fire After hooks
4. close the action window and collect the interrupt actions accumulated across all of that primitive's events, in evaluation order
5. one-shot hooks unregister themselves inside `OnEvent`, not as a separate consume step
6. insert interrupt responses ahead of the paused action
7. process the new queue head
8. emit presentation-facing notifications

When one primitive raises several events, hooks are evaluated per event in raise order, and the resulting interrupt actions are aggregated across all of that primitive's events before being reversed once — never reversed per event — so interrupts still insert in evaluation order (event 1's ahead of event 2's) at the front of the queue.

If hook ordering becomes contentious, tune the numeric priority argument given at registration. The important point is that ordering is data owned by the runtime, not incidental subscription order.

## Design Patterns

Useful patterns for this model:

- Command: primitive `BattleAction` values commit authoritative state changes.
- State Machine: each `BattleAction` tracks whether it is pending, running, paused, completed, cancelled, or failed.
- Mediator: `BattleActionExecutor` coordinates actions, events, hooks, and interruptions.
- Observer: hooks observe battle events through the registry, not through unmanaged subscriptions.
- Chain of Responsibility: ordered Before/After hook phases process events and return consequences.

## Open Design Questions

- Should visibility refresh remain part of primitive action success, move into the executor, or become a phase with very high priority?
- Should all actions be resumable, or only actions such as movement?
- Should reaction fire spend action points immediately when queued or when the shot commits?
- Should hooks return primitive actions directly, or only enqueue high-level actions?
- Should `BattleEvent` be split into rule-processing events and presentation events?

## Current Migration Result

1. `BattleAction` is the executor-facing action abstraction.
2. `MoveUnitStep` is the internal primitive movement commit.
3. Multi-step movement orchestration lives in `MoveUnit`.
4. The session's `BattleHookRegistry` stores hooks by `BattleEventTag` type and `HookPhase`.
5. Tile-based hooks can register against `TileOccupiedBattleEvent` (or a shared tag interface such as `IPositionedBattleEvent`).
6. Primitive actions publish committed events as they change state.
7. The session's dispatch loop fires Before/After hooks for every raised event and forwards any interrupt actions into the executor's open action window; the executor queues those response actions once the window closes.
8. Tests cover proof-of-concept hook responses such as damage after tile occupation.

This keeps the existing command/query/session architecture, but gives battle reactions a controlled place to run between primitive state changes.
