# BattleActionExecutor Design

This document describes the current role of `BattleActionExecutor` in the tactical combat runtime.

`BattleActionExecutor` is the interrupt-resolution and sequencing layer for queued `BattleAction` work.

## Purpose

The executor exists to answer two questions cleanly:

- what is the next primitive action that should execute?
- while it commits, which hook-returned interrupt actions should run before queued work continues?

It is responsible for:

- owning the pending action queue
- accepting a submitted `BattleAction` intent and resolving the resulting action chain
- executing one primitive action at a time from the queued action head
- opening an action window around each primitive's execution so hooks firing during its event dispatch can return interrupt actions, and collecting those interrupts once the primitive commits (discarding them if it fails)
- returning the ordered public `IReadOnlyList<BattleActionResult>` produced by that submission
- isolating exceptions raised during action execution

It is not responsible for:

- owning tactical truth
- rendering or animating battle actions
- pathfinding graph maintenance
- AI decision-making

## Authority Boundaries

- `BattleSession` remains the only owner of authoritative tactical state.
- `BattleAction` is the command object. It owns action-specific legality checks and execution against the session. It may be primitive, or it may unfold into primitive child actions.
- `BattleActionExecutor` invokes primitive actions produced by queued actions.
- scene controllers, HUD code, and AI build actions and submit them to the executor.
- visuals and HUD react to `BattleSession.BattleEventCommitted` after session state changes.

The important split is:

- `BattleAction` = queued tactical intent, which may be composite or primitive
- `BattleActionExecutor` = hook-interrupt mediator, queue, and invoker
- `BattleSession` = source of truth
- `BattleEvent` = notification that authoritative state already changed

## Current Public Surface

The current executor API is:

- `Submit(BattleAction action)`
- `LastResult`

And events:

- `OnActionStart`
- `OnActionComplete`

Hook registration is not part of this surface: hooks register on the session through the public `BattleRuntime.RegisterHook<TEventKey>(BattleHook hook, HookPhase phase, int priority = 0) where TEventKey : BattleEventTag` door, plus `UnregisterHook` for self-unregistering one-shots. The executor's only involvement is opening an action window around each primitive's execution so interrupts hooks return during that primitive's dispatch have somewhere to land — it does not own registration.

## Execution Flow

The current happy-path flow is:

1. controller or AI constructs a `BattleAction`
2. the action is submitted with `Submit(action)`
3. the executor queues the submitted action internally
4. while the queue has work, the executor asks the queue head for its next primitive action
5. the executor opens an action window for that primitive (`BeginActionExecution`) before calling its execution path
6. the action validates itself against the current `BattleSession`
7. the action applies its change through session and board helpers
8. `BattleSession` raises each resulting `BattleEvent`: the session's dispatch loop fires that event's `Before` hooks, broadcasts it through `BattleEventCommitted`, then fires its `After` hooks — any hook firing inside the open window may return interrupt actions, which accumulate in evaluation order
9. the executor closes the action window (`EndActionExecution`) once the primitive's execution path returns, whether it succeeded, failed, or threw
10. a failed primitive's collected interrupts are discarded; nothing is enqueued
11. on success, the executor aggregates the window's interrupt actions and reverses that combined list once (never per event), then inserts the result ahead of paused work — so an earlier-raised event's interrupts still run before a later event's
12. the executor continues until the submitted action and its interrupts settle
13. `Submit` returns every public `BattleActionResult` produced by that submission

Actions should be executed through an explicit `BattleActionExecutor` by calling `Submit`. A normal submission leaves the queue empty when it returns; the queue is non-empty only while the executor is actively resolving submitted work.

Composite actions can produce primitive child actions internally. Those child actions are commit checkpoints for validation, event emission, and hook evaluation; they are not reported as public action results. For example, `MoveUnit` may execute several `MoveUnitStep` commits, but `Submit` does not include those steps in its public result list.

Interrupt actions returned by hooks still raise `OnActionStart`, `OnActionComplete`, and committed `BattleEvent` notifications as they are resolved inside the same submission. `Submit` does not expose `Tick`; it queues the provided action, resolves the action and its interrupts immediately, and returns the public result sequence.

## Supported Validation Rules

Actions perform their own battle-legality validation before mutating session state. Current checks include battle phase, active side, unit availability, action-point cost, adjacency, board occupancy, throw range, path legality, and stale faction-turn requests.

The executor does not need to know which concrete action type it is running.

## Single-Threaded Execution

Authoritative battle action execution remains single-threaded.

Reasons:

- combat outcomes stay deterministic
- ordering stays explicit
- replay and debugging remain tractable
- interrupts and future sequencing are easier to reason about

Pure read-only work such as path previews or AI scoring can still move elsewhere later. The executor and `BattleSession` action path should remain single-threaded.

## Relationship To Visuals

Visual systems should not infer tactical truth from executor internals.

Recommended rule:

- use `BattleActionResult` for command outcomes
- use read-side `IBattleSessionQuery<TResult>` values for previews
- use `BattleEventCommitted` for visual updates after state has committed

Example:

- controller uses `FindPathForUnit` and related queries to decide whether to enable a move confirmation, then submits destination steps only to `MoveUnit`
- scene visuals listen for committed `UnitMovedBattleEvent`
- HUD refreshes AP and prompts after `OnActionComplete` or after relevant committed battle events

Godot scene nodes that need actual Godot signals should adapt this C# event at the presentation boundary. `BattleSession` stays a pure runtime object; a scene/controller can subscribe to `BattleEventCommitted` and re-emit a Godot signal with Variant-compatible payloads for animation code.

## Initial Query Surface

The executor does not expose preview evaluation. Controllers and AI should use explicit read-side queries, for example:

- reachable tiles for a unit (`GetPossibleMoveTilesForUnit`)
- a movement path preview (`FindPathForUnit`)
- a hit-chance preview for an attack (`GetHitChanceForAttack`)

Those should stay read-only and should not bypass the authoritative action path.
