# BattleRuntime Design

## Purpose

`BattleRuntime` is the main public interface for gameplay systems that need to interact with a battle. Player controllers, AI controllers, UI code, and scene orchestration should submit queries and actions through the runtime instead of reaching into `BattleSession` or constructing `BattleActionExecutor` directly.

This is a boundary refactor. It does not change battle rules, action semantics, query semantics, event payloads, turn sequencing, visibility behavior, or trigger behavior.

## Current Problem

The battle model already has useful separation:

- `BattleSession` owns battle state.
- `BattleSessionQuery<TResult>` describes read operations.
- `BattleQueryRunner` executes read operations against a session.
- `BattleAction` and `BattleActionExecutor` describe and execute state-changing operations.

The awkward part is the public usage shape. A caller may need to know about `BattleSession` to query state, and may need to construct or hold `BattleActionExecutor` to submit actions. That makes player and AI code depend on internal orchestration details.

OpenXCOM's Battlescape uses a useful command gateway pattern through `BattlescapeGame`, where UI and AI submit chosen actions to a central battle controller. It also exposes its saved battle state widely. This design adopts the gateway idea while keeping state access behind explicit query and action methods.

## Public API

`BattleRuntime` will expose query and action execution with names that make the operation type explicit:

```csharp
public sealed class BattleRuntime
{
  public BattleRuntime(BattleSession session);

  public Either<BattleQueryFailure, TResult> Query<TResult>(
    BattleSessionQuery<TResult> query);

  public IReadOnlyList<BattleActionResult> ExecuteAction(
    BattleAction action);

  public void RegisterTrigger(
    BattleTrigger trigger,
    BattleEventType eventType);

  public void RegisterTrigger(
    BattleTrigger trigger,
    IEnumerable<BattleEventType> eventTypes);

  public event Action<BattleEvent> BattleEventCommitted;
  public event Action<BattleAction> ActionStarted;
  public event Action<BattleActionResult> ActionCompleted;
}
```

`Query(...)` is the read path. `ExecuteAction(...)` is the state-changing command path.

`BattleRuntime` should not expose `BattleSession` publicly. Tests that need low-level access can continue constructing `BattleSession` and `BattleActionExecutor` directly.

## Internal Responsibilities

`BattleRuntime` owns:

- A `BattleSession` passed into the constructor.
- A `BattleQueryRunner` constructed from that session.
- A `BattleActionExecutor` constructed from that session.

`BattleRuntime.Query(...)` delegates to the owned query runner.

`BattleRuntime.ExecuteAction(...)` delegates to the owned action executor.

`BattleRuntime.RegisterTrigger(...)` delegates to the owned action executor so reaction and interruption setup does not require direct executor access.

The runtime forwards events from the session and executor:

- `BattleSession.BattleEventCommitted` to `BattleRuntime.BattleEventCommitted`.
- `BattleActionExecutor.OnActionStart` to `BattleRuntime.ActionStarted`.
- `BattleActionExecutor.OnActionComplete` to `BattleRuntime.ActionCompleted`.

Forwarding keeps UI, animation, logging, AI observers, and scene glue attached to the runtime boundary.

## Visibility and Encapsulation

`BattleSession` remains the state aggregate and may still be used by internal code and focused tests. It should not be the normal gameplay-facing object.

`BattleActionExecutor` remains the sequencing and trigger engine. It should not be the normal gameplay-facing object.

`BattleSession` should not expose a public `Queries` object. Query execution belongs to `BattleRuntime`, which creates the `BattleQueryRunner` for the session it owns.

Focused query tests can execute queries through `BattleRuntime`. Focused session tests can continue testing direct session behavior without going through the query runner.

No direct `runtime.Session` property should be added.

## Caller Shape

Gameplay callers should look like this:

```csharp
Either<BattleQueryFailure, IReadOnlyCollection<BattleBoardState.ValidatedPoint>> moveTiles =
  runtime.Query(new GetPossibleMoveTilesForUnit(unitHandle));

IReadOnlyList<BattleActionResult> results =
  runtime.ExecuteAction(BattleAction.MoveUnit(unitHandle, path));
```

This keeps callers aware of battle concepts, not battle storage or executor plumbing.

## Testing Strategy

Add focused `BattleRuntime` tests for:

- Query delegation returns the same result as the underlying session query runner.
- Action execution returns action results and mutates session state as expected.
- Trigger registration affects runtime action execution the same way executor trigger registration does.
- `BattleEventCommitted` is forwarded.
- `ActionStarted` is forwarded.
- `ActionCompleted` is forwarded.
- Constructor rejects a null session.
- `BattleSession` no longer exposes `Queries` as the public query path.

Then migrate representative higher-level tests to runtime usage. Good candidates are event and action-flow tests, because they model gameplay-facing behavior. Keep direct `BattleSession` and `BattleActionExecutor` tests where they verify those lower-level units specifically.

## Migration Strategy

1. Add `BattleRuntime`.
2. Add focused runtime tests.
3. Add a test helper for creating a runtime from common battle test setup.
4. Migrate gameplay-facing tests from direct executor/query access to runtime where it improves intent.
5. Leave low-level session, query, and executor tests direct.
6. Remove `BattleSession.Queries` and update query call sites to use `BattleRuntime.Query(...)`.

## Out Of Scope

This design does not add asynchronous action execution, animation state queues, player command builders, AI decision interfaces, networking, save/load changes, or new battle rules.

It also does not replace `BattleAction` with a different command type. Existing action classes remain the command payloads for now.
