# BattleSession Command Pattern

This document describes the command-pattern shape currently used around `BattleSession`, `BattleSessionMutation`, and `BattleActionExecutor`.

## Pattern Mapping

- `BattleSessionMutation` = command
- `BattleActionExecutor` = validator and invoker
- `BattleSessionQuery<TResult>` = read-side query command
- `BattleQueryRunner` = read-side query invoker
- `BattleQueryResult<TResult>` = explicit read-side success/failure result
- `BattleSession` = receiver and aggregate root

## Current Flow

```text
BattleSceneController / HUD / AI
  -> build BattleSessionMutation
  -> optional BattleActionExecutor.Evaluate(mutation)
  -> BattleActionExecutor.Enqueue(mutation) or ExecuteNow(mutation)
  -> mutation.ExecuteUnchecked(session)
  -> BattleSession bookkeeping APIs
  -> BattleEvent emission
```

The queue and replay surface stay focused on authoritative battle commands.

## Class Layout

```mermaid
classDiagram
    class BattleActionExecutor {
        -Queue~BattleSessionMutation~ _pending
        +Evaluate(BattleSessionMutation mutation) BattleActionEvaluation
        +ExecuteNow(BattleSessionMutation mutation) BattleMutationResult
        +Enqueue(BattleSessionMutation mutation)
        +EnqueueRange(IEnumerable~BattleSessionMutation~ mutations)
        +Tick() BattleMutationResult?
        +DrainQueue(int maxActions) IReadOnlyList~BattleMutationResult~
        +ActiveMutation BattleSessionMutation?
        +LastResult BattleMutationResult?
    }

    class BattleActionEvaluation {
        +Mutation BattleSessionMutation
        +IsAllowed bool
        +FailureReason BattleMutationFailureReason
        +Message string?
        +ActionPointCost int
    }

    class BattleSession {
        +Board BattleBoardState
        +Phase BattlePhase
        +TurnNumber int
        +ActiveSide Faction
        +Queries BattleQueryRunner
        +EventRaised Action~BattleEvent~
        <<receiver>>
    }

    class BattleSessionMutation {
        <<abstract command>>
        +MutationId string
        +Execute(BattleSession session) BattleMutationResult
        +ExecuteUnchecked(BattleSession session) BattleMutationResult
        #ExecuteCore(BattleSession session) BattleMutationResult
    }

    class BattleQueryRunner {
        +Execute~TResult~(BattleSessionQuery~TResult~ query) BattleQueryResult~TResult~
    }

    class BattleSessionQuery {
        <<abstract query>>
        +QueryId string
        #Execute(BattleSession session) BattleQueryResult~TResult~
    }

    class StartBattle
    class SpawnUnit
    class MoveUnitStep
    class MoveUnit
    class ThrowItem
    class ApplyDamage
    class PassUnit
    class EndFactionTurn

    BattleActionExecutor --> BattleActionEvaluation : returns
    BattleActionExecutor --> BattleSessionMutation : validates and invokes
    BattleSessionMutation --> BattleSession : executes against
    BattleQueryRunner --> BattleSessionQuery : invokes
    BattleSessionQuery --> BattleSession : reads from
    BattleSessionMutation <|-- StartBattle
    BattleSessionMutation <|-- SpawnUnit
    BattleSessionMutation <|-- MoveUnitStep
    BattleSessionMutation <|-- MoveUnit
    BattleSessionMutation <|-- ThrowItem
    BattleSessionMutation <|-- ApplyDamage
    BattleSessionMutation <|-- PassUnit
    BattleSessionMutation <|-- EndFactionTurn
```

## BattleSessionMutation

`BattleSessionMutation` is the command type used by the runtime.

The base class exposes:

```csharp
public abstract class BattleSessionMutation
{
  public string MutationId { get; }

  public BattleMutationResult Execute(BattleSession session)
  {
    ArgumentNullException.ThrowIfNull(session);
    return new BattleActionExecutor(session).ExecuteNow(this);
  }

  internal BattleMutationResult ExecuteUnchecked(BattleSession session)
  {
    ArgumentNullException.ThrowIfNull(session);
    var result = ExecuteCore(session);
    if (result.Succeeded)
      session.RefreshVisibility();

    return result;
  }

  protected abstract BattleMutationResult ExecuteCore(BattleSession session);
}
```

The important split is:

- the executor decides whether a supported mutation is legal right now
- the mutation applies the state change once execution begins
- the session keeps authoritative bookkeeping consistent

## BattleActionExecutor

`BattleActionExecutor` is now a real validator and command invoker.

It currently owns:

- mutation preview via `Evaluate(...)`
- pending mutation queue state
- execution ordering
- exception isolation around mutation execution
- last-result tracking

It does not own:

- selection logic
- tactical truth
- presentation state

## BattleSession Responsibilities

`BattleSession` remains the aggregate root and source of truth.

It owns:

- authoritative battle state
- alive and dead unit registration
- active faction and turn number
- faction order and round queue state
- availability for the current faction turn
- visibility refresh and bookkeeping
- event emission

`ActiveSide` is initialized from the configured faction order when the session is constructed; `Phase` determines whether that side is in setup, active battle, or ended state.

It should not own a command-type dispatch switch. Commands and the executor sit around the session; they do not replace it as the authority.

## Query And Commit Split

The current runtime works best when responsibilities stay narrow:

- scene controllers and AI ask battle-state questions through typed query objects
- preview code calls `BattleActionExecutor.Evaluate(...)`
- executor validates whether the mutation may run
- mutation code commits the change through session and board helpers
- `BattleSession` emits authoritative events

That split keeps controller code from duplicating legality rules while also keeping mutation application logic out of presentation code.

## Read Query Pattern

Read-side queries should mirror the command side without pretending reads are mutations.

The current shape is:

```csharp
public abstract class BattleSessionQuery<TResult>
{
  public string QueryId { get; }
  internal abstract BattleQueryResult<TResult> Execute(BattleSession session);
}

public sealed class BattleQueryRunner
{
  public BattleQueryResult<TResult> Execute<TResult>(BattleSessionQuery<TResult> query);
}
```

Controllers, HUD code, and AI should use:

```csharp
BattleQueryResult<IReadOnlyCollection<Vector3I>> moveTiles =
  session.Queries.Execute(new GetPossibleMoveTilesForUnit(unitId));

if (moveTiles is BattleQueryFailureResult<IReadOnlyCollection<Vector3I>> failure)
{
  // Handle failure.Failure before continuing.
}
```

This keeps `BattleSession` from accumulating public read methods such as `GetPossibleMoveTilesForUnit(...)`, `GetVisibleEnemiesForUnit(...)`, `CanThrowItemAt(...)`, and so on. Adding a battle-state question means adding a concrete query type and focused tests.

Guidelines:

- one query class answers one question
- query classes must be read-only
- query classes may compose session, board, visibility, and unit state
- invalid inputs and missing singular objects return `BattleQueryFailureResult<TResult>`
- empty collections remain successful results when there are no matching objects
- predicates return `BattleQueryFailureResult<bool>` when they cannot answer the question
- do not create catch-all query objects with mode enums or nullable selector fields
- use strategy objects inside individual queries when an algorithm has real variants, such as movement range or visibility

## Design Rules

- `BattleSession` stays the only owner of tactical truth.
- `BattleActionExecutor` owns legality checks for supported player-facing mutations.
- `BattleSessionMutation` owns command-specific application logic.
- `BattleQueryRunner` owns read-side query invocation.
- `BattleSessionQuery<TResult>` owns command-style read questions.
- command classes should not read or write session backing collections directly.
- query classes may read session internals but must not mutate session, board, units, or visibility snapshots.
- event emission should remain authoritative and happen through `BattleSession`.
- null-returning and failure-prone helpers should always be checked before continuing.

## Near-Term Extension Points

The next clean expansions on top of this pattern are:

- typed read-only query objects beside `Evaluate(...)`
- richer multi-step execution contexts for interrupts or reaction fire
- splitting large mutation files into one file per concrete command when the command set grows
