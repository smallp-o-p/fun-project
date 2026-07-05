# BattleSession Command Pattern

This document describes the command-pattern shape currently used around `BattleSession`, `BattleAction`, and `BattleActionExecutor`.

## Pattern Mapping

- `BattleAction` = command and queued tactical intent
- `BattleActionExecutor` = trigger mediator, queue, and invoker
- `IBattleSessionQuery<TResult>` = read-side query command
- `BattleRuntime.Query` = read-side query invoker
- `TResult` / `Option<T>` / `Either<BattleQueryFailure, TResult>` = read-side result, shaped by whether the question can fail
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

The queue and replay surface stay focused on explicit battle actions. Composite actions such as `MoveUnit` may yield internal primitive child actions one at a time. Primitive actions such as `ThrowItem` and `ApplyDamage` apply one authoritative state change. `Submit` queues the provided action, resolves that action and every reaction caused by it, then returns the ordered public result list.

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
- `RegisterTrigger<TEventKey>(BattleTrigger trigger) where TEventKey : BattleEventTag`
- `LastResult`

And events:

- `OnActionStart`
- `OnActionComplete`

`Submit` returns `IReadOnlyList<BattleActionResult>` from that submission. Composite child actions are not public results: `MoveUnitStep` results produced inside `MoveUnit` are hidden from callers. Trigger response actions still raise events and mutate battle state, but they are resolved before `Submit` returns.

Presentation code that needs committed state changes should subscribe to `BattleSession.BattleEventCommitted`. That callback runs after the corresponding state change has happened, so graphical work can query the session and see the committed state.

## Built-In Actions

The current built-in actions are:

- `StartBattle`
- `SpawnUnit`
- `MoveUnit`
- `AttackUnit`
- `ReloadWeapon`
- `ThrowItem`
- `ApplyDamage`
- `PassUnit`
- `EndFactionTurn`

`MoveUnit` is composite. Callers provide ordered, board-validated destination steps (`ValidatedPoint`s — in-bounds is proven at the caller's mint door), and the action checks the mutable route facts (adjacency, occupancy) from the unit's current session position before yielding an internal `MoveUnitStep` for each tile. This gives the executor a checkpoint where committed movement events can trigger reactions before the route continues inside the same submission. If one of those internal steps fails, the public result is a failed `MoveUnit`, not a failed `MoveUnitStep`.

## Read Side

Read-side queries should mirror the command side without pretending reads are actions.

Controllers and AI should use queries for read-only decisions, for example:

```csharp
runtime.Query(new GetPossibleMoveTilesForUnit(unit));
runtime.Query(new FindPathForUnit(unit, destination));
runtime.Query(new IsUnitStillAvailableThisTurn(unit));
```

A query's result shape is part of its contract: questions guaranteed an answer — usually via `AliveUnit`/`ValidatedPoint` proof inputs — return the bare result, absence-is-normal questions return `Option<T>`, and only genuinely fallible questions return `Either<BattleQueryFailure, TResult>`. Proofs are minted at the runtime boundary via `BattleRuntime.TryGetAlive` / `TryGetTile`.

Commands should remain explicit `BattleAction` values executed through `BattleActionExecutor.Submit`.
