# BattleSession Command Pattern

This document describes the command-pattern shape currently used around `BattleSession`, `BattleAction`, and `BattleActionExecutor`.

## Pattern Mapping

- `BattleAction` = command and queued tactical intent
- `BattleActionExecutor` = hook registry owner, hook-interrupt mediator, queue, and invoker
- `IBattleSessionQuery<TResult>` = read-side query command
- `BattleRuntime.Query` = read-side query invoker
- `BattleRuntime.ExecuteAction` = the total command door: `Some` when the submission ran, `None` when combat was already completed
- `TResult` / `Option<T>` / `Either<BattleQueryFailure, TResult>` = read-side result, shaped by whether the question can fail
- `BattleSession` = the running receiver: turn flow, damage/status pipelines, and terminal requests over its scheduler
- `BattleState` = the phase-independent tactical storage (board, pools, objectives, visibility, kill ledger) shared by preparation, running combat, and completion

## Current Flow

```text
BattleSceneController / HUD / AI
  -> build BattleAction
  -> BattleRuntime.ExecuteAction(action)
  -> the runtime opens the submission window, invokes the executor, and closes the window
  -> executor resolves the submitted action chain one primitive action at a time
  -> each primitive action validates and executes against BattleSession
  -> BattleSession bookkeeping APIs
  -> BattleEvent emission
  -> hooks fire from the executor's BattleEventCommitted subscription for each raised event (after the broadcast), inside the primitive's action window; the executor collects any interrupt actions they return before continuing
  -> a first accepted outcome finishes the current primitive and drains its synchronous event queue, then the executor captures, installs the frozen CompletedBattle, and broadcasts one SessionEndedBattleEvent; queued interrupts and later steps are dropped
```

The queue and replay surface stay focused on explicit battle actions. Composite actions such as `MoveUnit` commit one step per `Execute` (returning `Result.Incomplete` until the route drains). Primitive actions such as `ThrowItem` and `ApplyDamage` apply one authoritative state change. `ExecuteAction` resolves the action and every reaction caused by it, then returns one `BattleActionExecResult`: the submitted action plus every `BattleEvent` committed during its resolution. A submission against completed combat returns `None` without enqueueing work or emitting `ActionStarted`/`ActionCompleted`; the opening session/turn dispatch runs through the same internal orchestration (wrapped in a submission window, without public lifecycle signals).

## Runtime Split

- `BattleAction` owns action-specific application logic.
- `BattleAction` owns action-specific legality checks.
- `BattleActionExecutor` owns the hook registry (registering the default systems), queue order, hook-interrupt ordering, and exception isolation.
- `BattleState` owns phase-independent tactical storage; `BattleSession` is the running receiver layered over it with the scheduler, combat pipelines, and step-scoped terminal requests.
- `BattleEvent` reports state changes that already committed.
- queries read from the read context (state plus lifecycle answers) without mutating anything.

## Executor Surface

The executor's internal API is:

- `Execute(BattleAction action)` — internal; the runtime's `ExecuteAction` is the only production submission door
- `RegisterHook<TEventKey>(BattleHook hook, int priority = 0)` and `UnregisterHook<TEventKey>(BattleHook hook)` — facade doors live on `BattleRuntime`
- `Dispose()`

That is the whole surface: no lifecycle events, no `LastResult`. Outcomes are observed through the returned result's committed `BattleEvent` stream, the runtime's `ActionStarted`/`ActionCompleted` signals (which only fire for submissions that actually ran), and through committed session state.

Hooks register on the executor, not the session: the executor owns the `BattleHookRegistry`, registers the default systems in its constructor (status effects, armor regen, capability effects, buff evaluation, and `ObjectiveSystem`), and registers `ObjectiveSystem` once under the `BattleEventTag` catch-all at priority +100. It receives every event and filters objectives by their declared observed keys internally, with no self-registration or executor back-reference. An objective flips the moment a committed event makes its `Check` return Passed or Failed; its authored directive then interprets the flip. The executor fires matching hooks from its subscription to the shared committed stream once per committed event, after the broadcast. `BattleRuntime.RegisterHook` is the facade door delegating to it — `BattleSession` knows nothing about hooks; it only announces events. The executor opens an executor-local action window around each primitive so hook-returned interrupts have somewhere to land; a hook returning interrupts while no window is open throws. Exactly one executor per receiver is enforced: the runtime's construction path attaches its executor through the session's `AttachExecutor` door, which rejects a duplicate before it could subscribe to the committed stream or register any hook.

`ExecuteAction` returns the submission's `BattleActionExecResult` — `(Action, EventsThatOccurred)`, where `EventsThatOccurred` is a zero-copy `ReadOnlySpan<BattleEvent>` view into the executor's append-only event log. A composite's intermediate steps and hook-returned interrupt actions are not separate results; their committed events all belong to the submission's result. A first accepted outcome ends the loop: the current primitive and its synchronous event queue finish, remaining composite steps and queued interrupts are dropped, and the executor captures the frozen `CompletedBattle`, installs it, and broadcasts one `SessionEndedBattleEvent` before the submission returns. Parameters are trusted: a `Result.Rejected` (or a thrown exception) surfaces as an `InvalidOperationException` from `ExecuteAction` — always a caller bug, never a gameplay outcome — and a failed submission is unwound per the fault contract in [BattleActionExecutor Design](./battle-action-executor.md). Facts an earlier interrupt in the same submission can invalidate (liveness, possession, equipment, charges) are re-checked at `Execute` and drop the action as `Result.Interrupted`.

Presentation code that needs committed state changes should subscribe to `BattleRuntime.BattleEventCommitted`. That callback runs after the corresponding state change has happened, so graphical work can query the runtime and see the committed state.

## Built-In Actions

The current built-in actions are:

- `SpawnUnit` (running-battle reinforcement; initial placement belongs to preparation, not gameplay submissions)
- `MoveUnit`
- `AttackEntity`
- `ReloadWeapon`
- `ThrowItem`
- `UseItem`
- `ApplyDamage`
- `InteractWithObject`
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
runtime.Query(new GetCurrentTurnQuery());   // Option<BattleTurn>: Some while running, None after completion
runtime.Query(new GetCompletedBattleQuery()); // Option<CompletedBattle>: the frozen results once ended
```

A query's result shape is part of its contract: questions guaranteed an answer — usually via `AliveUnit`/`ValidatedPoint` proof inputs — return the bare result, absence-is-normal questions return `Option<T>`, and only genuinely fallible questions return `Either<BattleQueryFailure, TResult>`. The lifecycle questions are total: neither `GetCurrentTurnQuery` nor `GetCompletedBattleQuery` throws because of the lifecycle, and both derive from the runtime's one internal running-or-completed representation. Proofs are minted at the runtime boundary via `BattleRuntime.TryGetAlive` / `TryGetTile` / `TryGetAttackTarget`.

Commands remain explicit `BattleAction` values executed through `BattleRuntime.ExecuteAction`.
