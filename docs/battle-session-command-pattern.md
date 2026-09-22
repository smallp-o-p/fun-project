# BattleSession Command Pattern

This document describes the command-pattern shape currently used around `BattleSession`, `BattleAction`, and `BattleActionExecutor`.

## Pattern Mapping

- `BattleAction` = command and queued tactical intent
- `BattleActionExecutor` = hook registry owner, hook-interrupt mediator, queue, and invoker
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
  -> hooks fire from the executor's BattleEventCommitted subscription for each raised event (after the broadcast), inside the primitive's action window; the executor collects any interrupt actions they return before continuing
```

The queue and replay surface stay focused on explicit battle actions. Composite actions such as `MoveUnit` commit one step per `Execute` (returning `Result.Incomplete` until the route drains). Primitive actions such as `ThrowItem` and `ApplyDamage` apply one authoritative state change. `Submit` queues the provided action, resolves that action and every reaction caused by it, then returns one `BattleActionExecResult`: the submitted action plus every `BattleEvent` committed during its resolution.

## Runtime Split

- `BattleAction` owns action-specific application logic.
- `BattleAction` owns action-specific legality checks.
- `BattleActionExecutor` owns the hook registry (registering the default systems), queue order, hook-interrupt ordering, and exception isolation.
- `BattleSession` owns authoritative battle state.
- `BattleEvent` reports state changes that already committed.
- queries read from session state without mutating it.

## Executor Surface

The executor API is:

- `Submit(BattleAction action)`
- `RegisterHook<TEventKey>(BattleHook hook, int priority = 0)` and `UnregisterHook<TEventKey>(BattleHook hook)`
- `Dispose()`

That is the whole surface: no lifecycle events, no `LastResult`. Outcomes are observed through the returned result's committed `BattleEvent` stream and through committed session state.

Hooks register on the executor, not the session: the executor owns the `BattleHookRegistry`, registers the default systems in its constructor (status effects, armor regen, capability effects, buff evaluation, and `ObjectiveSystem`), and registers `ObjectiveSystem` once under the `BattleEventTag` catch-all at priority +100. It receives every event and filters objectives by their declared observed keys internally, with no self-registration or executor back-reference. An objective flips the moment a committed event makes its `Check` return Passed or Failed; its authored directive then interprets the flip. The executor fires matching hooks from its subscription to `session.BattleEventCommitted` once per committed event, after the broadcast. `BattleRuntime.RegisterHook` is the facade door delegating to it — `BattleSession` knows nothing about hooks; it only announces events. The executor opens an executor-local action window around each primitive so hook-returned interrupts have somewhere to land; a hook returning interrupts while no window is open throws. One executor per session is a hard invariant — a second executor would double-register the default systems (statuses ticking twice).

`Submit` returns the submission's `BattleActionExecResult` — `(Action, EventsThatOccurred)`, where `EventsThatOccurred` is a zero-copy `ReadOnlySpan<BattleEvent>` view into the executor's append-only event log. A composite's intermediate steps and hook-returned interrupt actions are not separate results; their committed events all belong to the submission's result. If a mid-dispatch objective directive or wipe backstop ends the battle, the executor drops remaining composite steps and queued interrupts instead of running them against an ended session. Parameters are trusted: a `Result.Rejected` (or a thrown exception) surfaces as an `InvalidOperationException` from `Submit` — always a caller bug, never a gameplay outcome — and a failed submission is fully unwound (no queued work survives it). Facts an earlier interrupt in the same submission can invalidate (liveness, possession, equipment, charges) are re-checked at `Execute` and drop the action as `Result.Interrupted`.

Presentation code that needs committed state changes should subscribe to `BattleSession.BattleEventCommitted`. That callback runs after the corresponding state change has happened, so graphical work can query the session and see the committed state.

## Built-In Actions

The current built-in actions are:

- `StartBattle`
- `SpawnUnit`
- `MoveUnit`
- `AttackEntity`
- `ReloadWeapon`
- `ThrowItem`
- `UseItem`
- `ApplyDamage`
- `PassUnit`
- `EndFactionTurn`

`MoveUnit` is composite. Callers provide ordered, board-validated destination steps (`ValidatedPoint`s — in-bounds is proven at the caller's mint door), and the action checks the mutable route facts (adjacency, occupancy) from the unit's current session position before committing each tile, returning `Result.Incomplete` between tiles. This gives the executor a checkpoint where committed movement events can trigger reactions before the route continues inside the same submission. If a reaction kills the mover, the remainder is interrupted quietly (`Result.Interrupted`); any other failed step is an invariant break that throws out of `Submit`.

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
