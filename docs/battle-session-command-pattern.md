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

`Submit` resolves the submitted action and every reaction it caused, then returns one `BattleActionExecResult`: the submitted action plus every `BattleEvent` committed during resolution, as a zero-copy `ReadOnlySpan<BattleEvent>` into the executor's append-only log. Composite actions such as `MoveUnit` commit one step per `Execute` (`Result.Incomplete` until no steps remain); primitives such as `ThrowItem` apply one authoritative state change. The submission's result includes events from intermediate steps and interrupt actions.

A mid-dispatch objective directive or the player-wipe defeat rule can end the battle. The executor then drops remaining composite steps and queued interrupts and ends execution. Parameters are trusted: only a `Result.Rejected` step is converted to an `InvalidOperationException` from `Submit`, because rejection is a caller bug. Arbitrary action or hook exceptions propagate unchanged after the queued work is cleared and availability is invalidated; committed state and events stay. Facts an earlier interrupt can invalidate (liveness, possession, equipment, charges) are re-checked at `Execute`; a stale action stops without an exception as `Result.Interrupted`.

## Runtime Split

- `BattleAction` owns action-specific legality checks and application logic.
- `BattleActionExecutor` owns the hook registry with the default systems, stack order, hook-interrupt ordering, and failure unwinding.
- `BattleSession` owns authoritative battle state and event dispatch.
- Queries return read-only results from session state.

## Executor Surface

- `Submit(BattleAction action)`
- `RegisterHook<TEventKey>(BattleHook hook, int priority = 0)` and `UnregisterHook<TEventKey>(BattleHook hook)`
- `Dispose()`

The executor owns the `BattleHookRegistry`, registers the default systems in its constructor, subscribes to `session.BattleEventCommitted`, and fires matching hooks once per committed event, after the broadcast. It opens an executor-local action window around each primitive. The window collects hook-returned interrupts; returning interrupts requires an in-flight action. Invalid returns throw. Hooks request queued actions through their return values; this convention relies on caller discipline.

`BattleRuntime.RegisterHook` is the facade method and delegates to the executor; the session announces events for the executor's hook dispatch. `BattleRuntime` owns the session's single executor; tests reuse it through `BattleFixture.Runtime`.

Presentation code that needs committed state changes subscribes to `BattleRuntime.BattleEventCommitted` (the re-raise, subscribed before the executor is constructed, so cause events arrive before follow-up events from hooks) or adapts `EventPlaybackDirector` for playback. The callback runs after the state change, so graphics can query the session and see committed state.

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

Initial placement is part of construction. The factory materializes the complete roster and objects before the session exists and announces them through its private bootstrap operation. `SpawnUnit` is the reinforcement action: it folds the new unit into live scheduling of an already-running battle.

`MoveUnit` takes ordered, board-validated destination steps (`ValidatedPoint`s; the runtime boundary proves the in-bounds property when it creates the proof). Its first `Execute` validates the whole route once (total AP cost, adjacency, occupancy) and caches the route. Each later `Execute` checks only mutable facts: the unit's liveness and incapacity, and the next cell's occupancy. Lost liveness or incapacity (unconsciousness and immobilization included) interrupts the remaining route without an exception. A next cell that a reaction occupied is a rejected invariant and throws. Committed steps remain checkpoints where reactions fire inside the same submission.

## Read Side

Typed queries express read-only questions:

```csharp
runtime.Query(new GetPossibleMoveTilesForUnit(unit));
runtime.Query(new FindPathForUnit(unit, destination));
runtime.Query(new IsUnitStillAvailableThisTurn(unit));
```

The result shape is part of the contract. Questions with a guaranteed answer, usually through `AliveUnit`/`ValidatedPoint` proof inputs, return the bare result. Questions with normal absence return `Option<T>`. Questions that can fail return `Either<BattleQueryFailure, TResult>`. Proofs are created at the runtime boundary via `BattleRuntime.TryGetAlive` / `TryGetTile` / `TryGetAttackTarget`. Commands stay explicit `BattleAction` values executed through `Submit`.

`GetAvailableActionsForUnit` returns the session cache's persistent read-only list for that unit identity; the list and each `UnitAction` stay live and keep their identity across submissions. Availability is cached per option and dirtied only by committed events that can affect its conditions (`UnitActionCondition` declares the per-condition dependencies), or by a failed submission; invalidation runs before event subscribers. During a submission, reads evaluate current state and preserve cached values.
