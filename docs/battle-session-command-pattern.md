# BattleSession Command Pattern

This document describes the command-pattern shape around `BattleSession`, `BattleAction`, and `BattleActionExecutor`, and the typed read side.

## Pattern Mapping

- `BattleAction` = command and tactical intent
- `BattleActionExecutor` = hook registry owner, interrupt mediator, action stack, invoker
- `IBattleSessionQuery<TResult>` = read-side query command; `BattleRuntime.Query` = its invoker
- `BattleSession` = receiver and aggregate root
- `BattleEvent` = report of a state change that already committed

## Current Flow

```text
BattleUiController / HUD / AI
  -> build BattleAction
  -> BattleRuntime.ExecuteAction / executor.Submit(action)
  -> executor resolves the submitted action chain one primitive at a time
  -> each primitive validates and executes against BattleSession
  -> session bookkeeping, then BattleEvent broadcast
  -> executor fires matching hooks after the broadcast, inside the primitive's action window
  -> hooks may return interrupt actions; the executor resolves them in the same submission
```

`Submit` resolves the submitted action and every reaction it caused. It returns one `BattleActionExecResult` that holds the submitted action plus every `BattleEvent` committed during resolution. The span is a zero-copy `ReadOnlySpan<BattleEvent>` into the executor's append-only log. Composite actions such as `MoveUnit` commit one step per `Execute` (`Result.Incomplete` until no steps remain). Primitives such as `ThrowItem` apply one authoritative state change. The submission's result includes events from intermediate steps and interrupt actions.

A mid-dispatch objective directive or the player-wipe defeat rule can end the battle. The executor then drops remaining composite steps and queued interrupts and ends execution.

Parameters are trusted. `Submit` converts only a `Result.Rejected` step to an `InvalidOperationException`. Rejection is a caller bug. The executor clears queued work and invalidates availability first. Arbitrary action or hook exceptions then propagate unchanged. Committed state and events stay.

Actions re-check facts an earlier interrupt can invalidate (liveness, possession, equipment, charges) at `Execute`. A stale action stops without an exception as `Result.Interrupted`.

## Runtime Split

- `BattleAction` owns action-specific legality checks and application logic.
- `BattleActionExecutor` owns the hook registry with the default systems, stack order, hook-interrupt ordering, and failure unwinding.
- `BattleSession` owns authoritative battle state and event dispatch.
- Queries return read-only results from session state.

## Executor Surface

- `Submit(BattleAction action)`
- `RegisterHook<TEventKey>(BattleHook hook, int priority = 0)` and `UnregisterHook<TEventKey>(BattleHook hook)`
- `Dispose()`

The executor owns the `BattleHookRegistry`. Its constructor registers the default systems. The executor subscribes to `session.BattleEventCommitted`. It fires matching hooks once per committed event, after the broadcast.

It opens an executor-local action window around each primitive. The window collects hook-returned interrupts. Returning interrupts requires an in-flight action. Invalid returns throw. Hooks request queued actions through their return values. This convention relies on caller discipline.

`BattleRuntime.RegisterHook` is the facade method and delegates to the executor. The session announces events for the executor's hook dispatch. `BattleRuntime` owns the session's single executor. Tests reuse it through `BattleFixture.Runtime`.

Presentation code that needs committed state changes subscribes to `BattleRuntime.BattleEventCommitted`. `BattleRuntime` re-raises that event. It subscribes before it constructs the executor, so cause events arrive before follow-up events from hooks. Presentation code can also adapt `EventPlaybackDirector` for playback. The callback runs after the state change, so graphics can query the session and see committed state.

## Built-In Actions

The current built-in actions are:
- `SpawnUnit`
- `MoveUnit` — composite
- `InteractWithObject`
- `AttackEntity`
- `ReloadWeapon`
- `ThrowItem`
- `UseItem`
- `ApplyDamage`
- `PassUnit`
- `EndFactionTurn`

Initial placement is part of construction. The factory materializes the complete roster and objects before the session exists and announces them through its private bootstrap operation. `SpawnUnit` is the reinforcement action. It folds the new unit into live scheduling of an already-running battle.

`MoveUnit` takes ordered, board-validated destination steps of type `ValidatedPoint`; the runtime boundary proves the in-bounds property when it creates the proof. Its first `Execute` validates the whole route once (total AP cost, adjacency, occupancy) and caches the route. Each later `Execute` checks only mutable facts: the unit's liveness and incapacity, and the next cell's occupancy. Lost liveness or incapacity (unconsciousness and immobilization included) interrupts the remaining route without an exception. A next cell that a reaction occupied is a rejected invariant and throws. Committed steps remain checkpoints where reactions fire inside the same submission.

## Read Side

Typed queries express read-only questions:

```csharp
runtime.Query(new GetPossibleMoveTilesForUnit(unit));
runtime.Query(new FindPathForUnit(unit, destination));
runtime.Query(new IsUnitStillAvailableThisTurn(unit));
```

The result shape is part of the contract. Questions with a guaranteed answer, usually through `AliveUnit`/`ValidatedPoint` proof inputs, return the bare result. Questions with normal absence return `Option<T>`. Questions that can fail return `Either<BattleQueryFailure, TResult>`. The runtime boundary creates proofs via `BattleRuntime.TryGetAlive` / `TryGetTile` / `TryGetAttackTarget`. Commands stay explicit `BattleAction` values that `Submit` executes.

`GetAvailableActionsForUnit` returns the session cache's persistent read-only list for that unit identity. The list and each `UnitAction` stay live and keep their identity across submissions. The session caches availability per option. Committed events dirty it only when they can affect its conditions (`UnitActionCondition` declares the per-condition dependencies); a failed submission also dirties it. Invalidation runs before event subscribers. During a submission, reads evaluate current state and preserve cached values.
