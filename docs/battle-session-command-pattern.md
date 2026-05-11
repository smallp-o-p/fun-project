# BattleSession Command Pattern

This document describes the command-pattern shape currently used around `BattleSession`, `BattleAction`, and `BattleActionExecutor`.

## Pattern Mapping

- `BattleAction` = command and queued tactical intent
- `BattleActionExecutor` = trigger mediator, queue, and invoker
- `BattleSessionQuery<TResult>` = read-side query command
- `BattleQueryRunner` = read-side query invoker
- `BattleQueryResult<TResult>` = explicit read-side success/failure result
- `BattleSession` = receiver and aggregate root

## Current Flow

```text
BattleSceneController / HUD / AI
  -> build BattleAction
  -> BattleActionExecutor.Submit(action)
  -> executor resolves the submitted action chain one primitive action at a time
  -> each primitive action validates and executes against BattleSession
  -> BattleSession bookkeeping APIs
  -> BattleEvent emission
  -> executor resolves trigger response actions from committed events before continuing
```

The queue and replay surface stay focused on explicit battle actions. Composite actions such as `MoveUnit` yield one primitive child action at a time. Primitive actions such as `MoveUnitStep`, `ThrowItem`, and `ApplyDamage` apply one authoritative state change. `Submit` queues the provided action, resolves that action and every reaction caused by it, then returns the ordered result list.

## Runtime Split

- `BattleAction` owns action-specific application logic.
- `BattleAction` owns action-specific legality checks.
- `BattleActionExecutor` owns queue order, trigger response ordering, and exception isolation.
- `BattleSession` owns authoritative battle state.
- `BattleEvent` reports state changes that already committed.
- queries read from session state without mutating it.

## Executor Surface

The executor API is:

- `Submit(BattleAction action)`
- `PendingCount`
- `LastResult`

And events:

- `OnActionStart`
- `OnActionComplete`

`Submit` returns `IReadOnlyList<BattleActionResult>` from that submission. Trigger response actions still raise events and mutate battle state, but they are resolved before `Submit` returns.

Presentation code that needs committed state changes should subscribe to `BattleSession.BattleEventCommitted`. That callback runs after the corresponding state change has happened, so graphical work can query the session and see the committed state.

## Built-In Actions

The current built-in actions are:

- `StartBattle`
- `SpawnUnit`
- `MoveUnitStep`
- `MoveUnit`
- `ThrowItem`
- `ApplyDamage`
- `PassUnit`
- `EndFactionTurn`

`MoveUnit` is composite. It validates that the unit is still alive and still on the expected path tile, then yields a `MoveUnitStep` for the next tile. This gives the executor a checkpoint where committed movement events can trigger reactions before the path continues inside the same submission.

## Read Side

Read-side queries should mirror the command side without pretending reads are actions.

Controllers and AI should use queries for read-only decisions, for example:

```csharp
session.Queries.Execute(new GetPossibleMoveTilesForUnit(unitHandle));
session.Queries.Execute(new FindPathForUnit(unitHandle, destination));
session.Queries.Execute(new IsUnitStillAvailableThisTurn(unitHandle));
```

Commands should remain explicit `BattleAction` values executed through `BattleActionExecutor.Submit`.
