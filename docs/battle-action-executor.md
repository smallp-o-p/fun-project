# BattleActionExecutor Design

This document describes the current role of `BattleActionExecutor` in the tactical combat runtime.

`BattleActionExecutor` is the trigger-resolution and sequencing layer for queued `BattleAction` work.

## Purpose

The executor exists to answer two questions cleanly:

- what is the next primitive action that should execute?
- after it commits, which trigger response actions should interrupt queued work?

It is responsible for:

- owning the pending action queue
- accepting a submitted `BattleAction` intent and resolving the resulting action chain
- executing one primitive action at a time from the queued action head
- resolving trigger responses from committed events after an action succeeds
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
- `BattleActionExecutor` = trigger mediator, queue, and invoker
- `BattleSession` = source of truth
- `BattleEvent` = notification that authoritative state already changed

## Current Public Surface

The current executor API is:

- `Submit(BattleAction action)`
- `RegisterTrigger(BattleTrigger trigger, BattleEventType eventType)`
- `RegisterTrigger(BattleTrigger trigger, IEnumerable<BattleEventType> eventTypes)`
- `PendingCount`
- `LastResult`

And events:

- `OnActionStart`
- `OnActionComplete`

## Execution Flow

The current happy-path flow is:

1. controller or AI constructs a `BattleAction`
2. the action is submitted with `Submit(action)`
3. the executor queues the submitted action internally
4. while the queue has work, the executor asks the queue head for its next primitive action
5. the executor captures `BattleEvent` values raised while the primitive action commits
6. the executor calls the primitive action's internal execution path
7. the action validates itself against the current `BattleSession`
8. the action applies its change through session and board helpers
9. `BattleSession` raises authoritative `BattleEvent` values through `BattleEventCommitted` after state changes
10. the executor evaluates triggers from those committed events
11. the executor inserts trigger response actions ahead of paused work
12. the executor continues until the submitted action and its reactions settle
13. `Submit` returns every public `BattleActionResult` produced by that submission

Actions should be executed through an explicit `BattleActionExecutor` by calling `Submit`. A normal submission leaves the queue empty when it returns; the queue is non-empty only while the executor is actively resolving submitted work.

Composite actions can produce primitive child actions internally. Those child actions are commit checkpoints for validation, event emission, and trigger evaluation; they are not reported as public action results. For example, `MoveUnit` may execute several `MoveUnitStep` commits, but `Submit` does not include those steps in its public result list.

Trigger response actions still raise `OnActionStart`, `OnActionComplete`, and committed `BattleEvent` notifications as they are resolved inside the same submission. `Submit` does not expose `Tick`; it queues the provided action, resolves the action and reactions immediately, and returns the public result sequence.

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
- use read-side `BattleSessionQuery<TResult>` values for previews
- use `BattleEventCommitted` for visual updates after state has committed

Example:

- controller uses `FindPathForUnit` and related queries to decide whether to enable a move confirmation, then submits destination steps only to `MoveUnit`
- scene visuals listen for committed `UnitMovedBattleEvent`
- HUD refreshes AP and prompts after `OnActionComplete` or after relevant committed battle events

Godot scene nodes that need actual Godot signals should adapt this C# event at the presentation boundary. `BattleSession` stays a pure runtime object; a scene/controller can subscribe to `BattleEventCommitted` and re-emit a Godot signal with Variant-compatible payloads for animation code.

## Initial Query Surface

The executor does not expose preview evaluation. Controllers and AI should use explicit read-side queries, for example:

- reachable cells for a unit
- valid throw targets for an item
- action-point cost for a path

Those should stay read-only and should not bypass the authoritative action path.
