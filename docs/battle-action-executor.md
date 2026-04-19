# BattleActionExecutor Design

This document describes the current role of `BattleActionExecutor` in the tactical combat runtime.

`BattleActionExecutor` is the validation and invocation layer for `BattleSessionMutation`.

## Purpose

The executor exists to answer two questions cleanly:

- can this mutation execute against the current battle state?
- if it can, when and in what order should it execute?

It is responsible for:

- validating supported player-facing mutations against current session state
- owning the pending mutation queue
- executing at most one queued mutation per `Tick()`
- returning structured `BattleMutationResult` and `BattleActionEvaluation` values
- isolating exceptions raised during mutation execution

It is not responsible for:

- owning tactical truth
- rendering or animating battle actions
- pathfinding graph maintenance
- AI decision-making

## Authority Boundaries

- `BattleSession` remains the only owner of authoritative tactical state.
- `BattleSessionMutation` remains the command object that applies a state change.
- `BattleActionExecutor` validates and invokes mutations.
- scene controllers, HUD code, and AI build mutations and submit them to the executor.
- visuals and HUD react to `BattleEvent` after session state changes.

The important split is:

- `BattleSessionMutation` = executable command
- `BattleActionExecutor` = validator, queue, and invoker
- `BattleSession` = source of truth
- `BattleEvent` = notification that authoritative state already changed

## Current Public Surface

The current executor API is:

- `Evaluate(BattleSessionMutation mutation)`
- `ExecuteNow(BattleSessionMutation mutation)`
- `Enqueue(BattleSessionMutation mutation)`
- `EnqueueRange(IEnumerable<BattleSessionMutation> mutations)`
- `Tick()`
- `DrainQueue(int maxActions = int.MaxValue)`
- `PendingCount`
- `ActiveMutation`
- `LastResult`

And events:

- `MutationStarted`
- `MutationResolved`

`Evaluate(...)` is the preview-friendly entry point. Controllers can build a mutation, ask the executor whether it is legal, inspect the rejection message or action-point cost, and only then enqueue or execute it.

## Evaluation Shape

`BattleActionEvaluation` currently reports:

- the mutation being evaluated
- whether the mutation is allowed
- the failure reason when rejected
- an optional message
- the total action-point cost when that concept applies

This keeps UI and AI code on the same validation path as actual execution.

## Execution Flow

The current happy-path flow is:

1. controller or AI constructs a `BattleSessionMutation`
2. optional preview code calls `Evaluate(mutation)`
3. the mutation is queued with `Enqueue(...)` or executed immediately with `ExecuteNow(...)`
4. the executor validates the mutation against the current `BattleSession`
5. the executor calls `mutation.ExecuteUnchecked(session)` if the mutation is legal
6. the mutation applies its change through session and board helpers
7. `BattleSession` raises authoritative `BattleEvent` values
8. the executor returns a `BattleMutationResult`

`BattleSessionMutation.Execute(session)` also routes through the executor so direct mutation execution uses the same validation path.

## Supported Validation Rules

The executor currently performs battle-legality validation for these mutations:

- `MoveUnitStep`
- `MoveUnit`
- `ThrowItem`
- `PassUnit`
- `EndFactionTurn`

These checks include battle phase, active side, unit availability, action-point cost, adjacency, board occupancy, throw range, and path legality.

Other mutations currently pass through without extra executor-side legality rules:

- `StartBattle`
- `SpawnUnit`
- `ApplyDamage`

Those mutations still protect their own invariants during application.

## Single-Threaded Execution

Authoritative battle mutation remains single-threaded.

Reasons:

- combat outcomes stay deterministic
- ordering stays explicit
- replay and debugging remain tractable
- interrupts and future sequencing are easier to reason about

Pure read-only work such as path previews or AI scoring can still move elsewhere later. The executor and `BattleSession` mutation path should remain single-threaded.

## Relationship To Visuals

Visual systems should not infer tactical truth from executor internals.

Recommended rule:

- use `BattleActionEvaluation` and `BattleMutationResult` for controller logic
- use `BattleEvent` for visual updates

Example:

- controller uses `Evaluate(...)` to decide whether to enable a move confirmation
- scene visuals listen for `UnitMoved`
- HUD refreshes AP and prompts after `MutationResolved` or after relevant battle events

## Initial Query Surface

The executor now provides one general preview query:

- `Evaluate(BattleSessionMutation mutation)`

As the tactics layer grows, additional explicit read APIs can be added alongside it, for example:

- reachable cells for a unit
- valid throw targets for an item
- action-point cost for a path

Those should stay read-only and should not bypass the authoritative mutation path.
