# Battle Event Trigger Architecture Draft

This document sketches a possible tactical runtime architecture for event-driven battle reactions. It is a draft proposal, not a description of the current implementation.

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

These event types describe committed state changes. Consequences are resolved from committed events captured by the action executor immediately after a primitive action succeeds.

## Initial Implementation

The first implementation slice now exists in code:

- `BattleEventType.TileOccupied` is emitted after successful movement steps.
- `MoveUnitStep` publishes committed `UnitMoved` and `TileOccupied` after a successful move.
- `ApplyDamage` publishes committed `UnitDamaged` and lethal `UnitKilled`.
- `EndFactionTurn` publishes committed `TurnEnded` and `TurnStarted` transition events.
- `BattleTrigger` and the executor's internal trigger registry provide mediated, deterministic trigger evaluation with event-type buckets.
- `BattleAction` and `MoveUnit` give the executor explicit high-level queued work and primitive action commits.
- `BattleActionExecutor` accepts a submitted `BattleAction`, commits primitive actions until the submitted action and its reactions settle, resolves trigger responses from committed events, and inserts interrupt response actions ahead of paused work.

Current limitations:

- Concrete overwatch and proximity mine rules are not implemented yet; tests use simple damage triggers as proof-of-concept trigger responses.
- Runtime trigger responses require a persistent `BattleActionExecutor` when a composite action needs to pause and resume around reactions.
- The old predictive command surfaces have been removed. Queued player/AI intent must be expressed as `BattleAction` values such as `MoveUnit`, and primitive actions are the commit layer.
- The executor indexes triggers by event type. It does not yet index by tile, faction, owner, or source item.

## Core Runtime Shape

```text
Controller / AI
  -> BattleAction
  -> BattleActionExecutor.Submit(action)
  -> inspect the action at the head of the queue
  -> ask the head action for its next primitive action
  -> validate and commit that primitive action
  -> capture the committed events raised during the commit
  -> ask registered triggers whether any committed event matters
  -> enqueue any trigger response actions
  -> continue until the submitted action and reactions settle
  -> return the ordered action result list
```

The important distinction is between composite actions and primitive actions.

- A high-level action is intent, such as "move this unit through these destination tiles."
- A primitive action is an atomic authoritative state change, such as "move this unit from tile A to adjacent tile B."

Longer actions should be modeled as sequences of primitive actions. This gives the runtime a stable checkpoint after each step where visibility, triggers, reactions, and presentation updates can happen.

The executor should be understood as an action queue processor. The queue head is the currently executing or about-to-execute action. Trigger responses are also actions. When the head action commits events that match registered triggers, the executor enqueues the trigger response actions so they run before the interrupted head action continues. Those response actions can commit events of their own, which may enqueue more actions.

## Committed Event Reaction Planning

Battle observers are consulted after a primitive state change commits.

The primitive action is responsible for raising the domain events caused by its authoritative state change. The executor captures those committed events, resolves triggers in a deterministic order, and queues any requested responses.

For movement, this means:

```text
validate MoveUnitStep
  -> commit the unit onto the destination tile
  -> publish committed UnitMoved and TileOccupied
  -> executor finds triggers for those committed events
  -> run planned interrupt while the unit is on that tile
  -> continue, interrupt, or cancel the remaining movement
```

This matters for overwatch and mines. If a unit steps from `X` to `X + 1`, the reaction should evaluate against the post-step state where the unit occupies `X + 1`. The longer move to `X + N` is still unresolved and can be paused or cancelled.

Rules that prevent a commit, such as "this tile cannot be entered," should remain validation rules. Rules that react to an occurred fact, such as "a watched tile became occupied," should be trigger rules. Trigger response actions run after the triggering primitive action commits, so they act on the committed state that caused the event.

For overwatch, the trigger is discovered from the committed movement event, so the target is physically on the watched tile when the shot executes. For a proximity mine, detonation likewise happens after the unit has entered the triggering tile.

## Proposed Components

### BattleAction

`BattleAction` represents work requested by a player, AI, reaction, or system.

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
- insert trigger response actions ahead of a paused action when they must resolve immediately
- queue follow-up actions in defined positions when they do not interrupt the active action
- resolve the submitted action chain one primitive commit at a time
- capture committed events during each primitive commit
- resolve registered triggers for committed events
- queue deterministic trigger response actions
- decide whether the active action continues, pauses, cancels, or completes
- return the ordered public result list from the submission

The executor should still be single-threaded. It is a scheduler and resolver, not a parallel event bus.

A useful mental model:

```text
queue: [MoveUnit]

MoveUnit step commits TileOccupied
  -> overwatch trigger returns ReactionFire
  -> executor inserts ReactionFire before the remaining MoveUnit work

queue while resolving: [ReactionFire, MoveUnit]

ReactionFire may commit UnitDamaged
  -> damage trigger may insert another response
```

The queue can therefore grow while a submission is being resolved. The executor controls where new actions are inserted so nested reactions remain deterministic, and a normal submission leaves the queue empty when it returns.

The public result list does not include primitive child actions produced by a composite action. A `MoveUnit` that commits multiple `MoveUnitStep` actions hides those step results from callers. The committed `BattleEvent` stream remains the source for per-step movement and trigger details.

The executor may publish presentation notifications after rule resolution, but it should not be the source of truth for domain events. Primitive actions and the session emit committed `BattleEvent` values as they change authoritative state.

### Primitive BattleAction

Primitive `BattleAction` types are the commit layer.

Primitive actions own their state changes and event emission because they know exactly what changed. For example, `MoveUnitStep` knows the source tile, destination tile, and moved unit after it changes board occupancy. `ApplyDamage` knows the affected unit, damage amount, and whether the damage reduced health to zero.

The executor should not infer events by diffing session state. It should capture the committed domain events raised while a primitive action executes, then use those events to resolve triggers.

Good primitive actions:

- `MoveUnitStep`
- `ApplyDamage`
- `KillUnit`
- `StartBattle`
- `EndFactionTurn`

High-level movement should be represented by composite actions such as `MoveUnit`. The primitive movement commit should remain `MoveUnitStep`.

### BattleEvent

`BattleEvent` describes a committed battle fact. The base event carries the stable `BattleEventType` used for trigger registration, while concrete event subclasses carry only the payload that applies to that event.

Events need enough context for triggers to evaluate without guessing:

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
  Position
  Amount

UnitKilledBattleEvent
  UnitId
  Position

TurnStartedBattleEvent / TurnEndedBattleEvent
  Faction
  TurnNumber
```

Shared interfaces such as `IPositionedBattleEvent` let triggers match common payload concepts without forcing every event into one large optional-field record.

### BattleTrigger

`BattleTrigger` is registered battle-state behavior that reacts to events.

Examples:

- a proximity mine registered against affected tiles
- an overwatch cone registered against watched tiles
- a reaction-fire rule registered against visibility or occupation events
- a hazard registered against burning or electrified tiles

A trigger should be serializable battle state. It should not be a raw runtime delegate that disappears on save/load.

```text
BattleTrigger
  TriggerId
  Owner
  WatchedEventTypes
  WatchedTiles
  Priority
  Expiration
```

Triggers should evaluate committed events and return requested consequences. They should not directly mutate `BattleSession`.

```text
Trigger evaluation result:
  - no reaction
  - enqueue response action after commit
  - consume this trigger
```

The normal response for event observers is "after commit." This means an event such as `TileOccupied` is captured after the board state changes, and any response action such as overwatch fire or mine detonation is applied while the tile is actually occupied.

### Trigger Registration

`BattleActionExecutor` owns registered triggers and provides efficient lookup through an internal registry. Registration chooses the event-type bucket:

```csharp
executor.RegisterTrigger(trigger, BattleEventType.TileOccupied);
executor.RegisterTrigger(trigger, new[] { BattleEventType.UnitMoved, BattleEventType.TileOccupied });
```

`BattleTrigger` still owns detailed matching through `Matches(...)`, but the registry only calls `Matches(...)` on triggers registered for the committed event type.

For tile-based events, it should be able to answer:

```text
Which triggers care about a committed TileOccupied at tile X?
```

This supports proximity mines and overwatch without broadcasting every event to every possible observer.

The registry currently indexes by:

- event type

Later indices can include:

- tile
- owning unit
- source item
- faction

This supports the current runtime without broadcasting every event to every trigger, while leaving room for spatial indexing if tile-based trigger counts grow.

## Observer Rule

Observers should be event-driven, but mediated.

Prefer this:

```text
event committed -> registry finds matching triggers -> executor queues trigger responses
```

Avoid this as the authoritative rule mechanism:

```text
event emitted -> arbitrary C# event subscribers mutate battle state
```

The mediated model keeps event-driven behavior while preserving deterministic ordering, save/load friendliness, and debuggability.

## Movement And Overwatch Flow

Example: a unit moves from tile `X` toward tile `X + N`, and an enemy has overwatch over an intermediate tile.

```text
1. Player or AI enqueues MoveUnit(unit, destinations: X + 1 -> ... -> X + N), using the unit's current session position as the route source.
2. Executor starts the move action.
3. Executor validates and commits MoveUnitStep(unit, X, X + 1).
4. Session updates board occupancy and unit position.
5. MoveUnitStep raises committed UnitMoved and TileOccupied for X + 1.
6. Executor refreshes visibility and evaluates triggers for the committed events.
7. Overwatch trigger verifies that the committed target is valid.
8. Trigger returns QueueInterruptAfterCommit(ReactionFire(observer, target)).
9. Executor pauses MoveUnit and inserts ReactionFire at the head of the queue.
10. ReactionFire runs while the target is on X + 1.
11. ReactionFire applies damage if the shot hits.
12. Damage events may enqueue more response actions.
13. If the moving unit dies, the paused move action is cancelled.
14. If the moving unit survives, the move action resumes with the next step.
```

The target is therefore shot while occupying the trigger tile, not after the whole path has completed.

## Proximity Mine Flow

Example: a proximity mine is placed and listens for occupation of nearby tiles.

```text
1. Mine is placed.
2. Session registers ProximityMineTrigger with affected tiles.
3. Unit attempts to move onto an affected tile.
4. MoveUnitStep commits and raises TileOccupied for that tile.
5. Trigger registry finds the mine trigger for the committed tile.
6. Mine trigger verifies it is armed and the occupying unit is a valid target.
7. Trigger returns QueueInterruptAfterCommit(DetonateMine) and ConsumeTrigger.
8. Executor consumes the trigger.
9. Executor pauses the movement action and inserts DetonateMine at the head of the queue.
10. DetonateMine runs before the movement action continues.
11. DetonateMine publishes committed UnitDamaged or UnitKilled events.
12. Mine damage may enqueue more response actions.
13. If the mover survives and movement is still valid, the original action continues.
```

The mine itself is registered battle state, not a scene object. Presentation can subscribe to the same committed events to animate the detonation.

## Visibility Flow

Visibility should be updated at primitive checkpoints, especially after movement.

```text
MoveUnitStep commits
  -> committed UnitMoved / TileOccupied events are published and captured
  -> visibility is refreshed
  -> triggers that depend on post-step state are resolved
  -> newly visible or newly hidden units can emit visibility events later
```

The starting event list does not include visibility events yet, but the model can add them later:

- `UnitSpotted`
- `UnitLostFromSight`
- `TileRevealed`

For now, overwatch can use the committed event to discover that a watched tile became occupied, then validate against the current `BattleVisibilitySnapshot` before the reaction shot executes.

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

The executor should own the order in which triggers resolve. Initial ordering can be simple and explicit:

1. ask the active action for its next primitive action
2. validate the primitive action
3. commit the primitive action
4. capture committed domain events
5. refresh derived state such as visibility
6. resolve triggers that care about those committed events
7. consume resolved one-shot triggers
8. insert interrupt responses ahead of the paused action
9. process the new queue head
10. emit presentation-facing notifications

If trigger ordering becomes contentious, add numeric `Priority` to trigger definitions. The important point is that ordering is data owned by the runtime, not incidental subscription order.

## Design Patterns

Useful patterns for this model:

- Command: primitive `BattleAction` values commit authoritative state changes.
- State Machine: each `BattleAction` tracks whether it is pending, running, paused, completed, cancelled, or failed.
- Mediator: `BattleActionExecutor` coordinates actions, events, triggers, and interruptions.
- Observer: triggers observe battle events through the registry, not through unmanaged subscriptions.
- Chain of Responsibility: ordered trigger systems process events and return consequences.

## Open Design Questions

- Should visibility refresh remain part of primitive action success, move into the executor, or become a trigger system with very high priority?
- Should all actions be resumable, or only actions such as movement?
- Should reaction fire spend action points immediately when queued or when the shot commits?
- Should triggers return primitive actions directly, or only enqueue high-level actions?
- Should `BattleEvent` be split into rule-processing events and presentation events?

## Current Migration Result

1. `BattleAction` is the executor-facing action abstraction.
2. `MoveUnitStep` is the internal primitive movement commit.
3. Multi-step movement orchestration lives in `MoveUnit`.
4. The executor stores triggers by committed event type.
5. Tile triggers can register against `TileOccupied`.
6. Primitive actions publish committed events as they change state.
7. The executor resolves triggers from committed events and queues response actions.
8. Tests cover proof-of-concept trigger responses such as damage after tile occupation.

This keeps the existing command/query/session architecture, but gives battle reactions a controlled place to run between primitive state changes.
