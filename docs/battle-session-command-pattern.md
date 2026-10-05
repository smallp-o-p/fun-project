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

`Submit` resolves the submitted action and every reaction it caused, then returns one `BattleActionExecResult`: the submitted action plus every `BattleEvent` committed during resolution, as a zero-copy `ReadOnlySpan<BattleEvent>` into the executor's append-only log. Composite actions such as `MoveUnit` commit one step per `Execute` (`Result.Incomplete` until the route drains); primitives such as `ThrowItem` apply one authoritative state change. Intermediate steps and interrupt actions are not separate results; their events belong to the submission's result.

If a mid-dispatch objective directive or wipe backstop ends the battle, the executor drops remaining composite steps and queued interrupts instead of running them against an ended session. Parameters are trusted: only a `Result.Rejected` step is converted to an `InvalidOperationException` from `Submit` — a caller bug, never a gameplay outcome. Arbitrary action or hook exceptions propagate unchanged after the queued work is cleared and availability is invalidated; committed state and events stay. Facts an earlier interrupt can invalidate (liveness, possession, equipment, charges) are re-checked at `Execute`; a stale action stops quietly as `Result.Interrupted`.

## Runtime Split

- `BattleAction` owns action-specific legality checks and application logic.
- `BattleActionExecutor` owns the hook registry with the default systems, stack order, hook-interrupt ordering, and failure unwinding.
- `BattleSession` owns authoritative battle state and event dispatch.
- Queries read session state without mutating it.

## Executor Surface

- `Submit(BattleAction action)`
- `RegisterHook<TEventKey>(BattleHook hook, int priority = 0)` and `UnregisterHook<TEventKey>(BattleHook hook)`
- `Dispose()`

That is the whole surface: no lifecycle events, no `LastResult`. Hooks register on the executor, not the session. The executor owns the `BattleHookRegistry`, registers the default systems in its constructor, subscribes to `session.BattleEventCommitted`, and fires matching hooks once per committed event, after the broadcast. It opens an executor-local action window around each primitive so hook-returned interrupts have somewhere to land; a hook returning interrupts with no action in flight throws. Hooks never call `executor.Submit` inside `OnEvent` — that is a convention, with no guard.

`BattleRuntime.RegisterHook` is the facade door delegating to the executor; the session knows nothing about hooks and only announces events. One executor per session is an invariant held by ownership: a second live executor would double-register the default systems, so statuses would tick twice. Production `BattleRuntime` owns that executor; tests reuse `BattleFixture.Runtime`.

Presentation code that needs committed state changes subscribes to `BattleRuntime.BattleEventCommitted` (the re-raise, subscribed before the executor is constructed, so cause events arrive before hook-born follow-ups) or adapts `EventPlaybackDirector` for playback. The callback runs after the state change, so graphics can query the session and see committed state.

## Built-In Actions

- `StartBattle`
- `SpawnUnit`
- `MoveUnit` — composite
- `PlaceObject`
- `InteractWithObject`
- `AttackEntity`
- `ReloadWeapon`
- `ThrowItem`
- `UseItem`
- `ApplyDamage`
- `PassUnit`
- `EndFactionTurn`

`MoveUnit` takes ordered, board-validated destination steps (`ValidatedPoint`s — in-bounds is proven at the mint door). Its first `Execute` validates the whole route once — total AP cost, adjacency, occupancy — and caches it. Each later `Execute` checks only mutable facts: the unit's liveness and incapacity, and the next cell's occupancy. Lost liveness or incapacity (unconsciousness and immobilization included) interrupts the remaining route quietly. A next cell that a reaction occupied is a rejected invariant and throws. Committed steps remain checkpoints where reactions fire inside the same submission.

## Read Side

Read-side queries mirror the command side without pretending reads are actions:

```csharp
runtime.Query(new GetPossibleMoveTilesForUnit(unit));
runtime.Query(new FindPathForUnit(unit, destination));
runtime.Query(new IsUnitStillAvailableThisTurn(unit));
```

The result shape is contract: questions guaranteed an answer — usually through `AliveUnit`/`ValidatedPoint` proof inputs — return the bare result; absence-is-normal questions return `Option<T>`; genuinely fallible questions return `Either<BattleQueryFailure, TResult>`. Proofs are minted at the runtime boundary via `BattleRuntime.TryGetAlive` / `TryGetTile` / `TryGetAttackTarget`. Commands stay explicit `BattleAction` values executed through `Submit`.

`GetAvailableActionsForUnit` returns the session cache's persistent read-only list for that unit identity; the list and each `UnitAction` stay live and keep their identity across submissions. Availability is cached per option and dirtied only by committed events that can affect its conditions (`UnitActionCondition` declares the per-condition dependencies), or by a failed submission; invalidation runs before event subscribers. During a submission, reads evaluate current state without overwriting cached values.
