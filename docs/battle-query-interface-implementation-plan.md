# Battle Query Interface Implementation Plan

This plan tracks the refactor from the transitional `IBattleSessionQuery` facade to typed read-side query objects with explicit success/failure results.

## Goal

Controllers, HUD code, AI, and tests should ask battle-state questions by executing concrete query objects:

```csharp
BattleQueryResult<Vector3I[]> pathResult =
  session.Queries.Execute(new FindPathForUnit(unitId, destination));

if (pathResult is BattleQuerySuccess<Vector3I[]> pathSuccess)
{
  Vector3I[] path = pathSuccess.Value;
}
```

`BattleSession` remains the authoritative state owner. Query objects are read-only and do not bypass the mutation path for state changes.

## Target Shape

Add query infrastructure under `scripts/battle/queries/`:

- `BattleSessionQuery<TResult>`
- `BattleQueryRunner`
- `BattleQueryResult<TResult>`
- `BattleQuerySuccess<TResult>`
- `BattleQueryFailureResult<TResult>`
- `BattleQueryFailure`
- one concrete query class per battle-state question

`BattleSession` should expose:

```csharp
public BattleQueryRunner Queries { get; }
```

## Query Types To Introduce First

Start with the queries already represented by the transitional facade:

- `GetUnit`
- `GetLivingUnit`
- `GetFactionAliveUnits`
- `GetFactionDeadUnits`
- `CanUnitActNow`
- `IsUnitStillAvailableThisTurn`
- `CanOccupyTile`
- `FindPathForUnit`
- `GetPossibleMoveTilesForUnit`
- `IsUnitVisibleToUnit`
- `IsUnitVisibleToFaction`
- `IsTileVisibleToFaction`
- `HasFactionExploredTile`
- `GetVisibleUnitsForUnit`
- `GetVisibleEnemiesForUnit`
- `GetVisibleUnitsForFaction`
- `GetVisibleTilesForFaction`
- `GetExploredTilesForFaction`

Do not combine these into one configurable query class. Each concrete type should encode its inputs through constructor parameters.

## Implementation Phases

1. Add query infrastructure.
   - Create `scripts/battle/queries/BattleSessionQuery.cs`.
   - Create `scripts/battle/queries/BattleQueryRunner.cs`.
   - Create explicit query result and failure types.
   - Add `BattleSession.Queries`.
   - Keep the current `BattleSession.Query` facade temporarily if needed for incremental migration.

2. Move simple unit and availability queries.
   - Implement unit lookup, faction unit lists, `CanUnitActNow`, and `IsUnitStillAvailableThisTurn`.
   - Update `BattleActionExecutor` and focused tests to call `session.Queries.Execute(...)`.
   - Missing singular lookups return `BattleQueryFailureResult<T>`.
   - Predicates that cannot resolve their inputs return `BattleQueryFailureResult<bool>`.
   - Empty collections remain the successful result for "no matching objects."

3. Move movement queries.
   - Implement `FindPathForUnit`.
   - Implement `GetPossibleMoveTilesForUnit`.
   - Keep board-owned AStar details inside `BattleBoardState`.
   - Query objects should compose unit state and board pathfinding without mutating either.

4. Move visibility and fog queries.
   - Implement per-unit visibility, per-faction visibility, visible enemies, visible tiles, and explored tiles.
   - Queries should read the current `BattleVisibilitySnapshot`.
   - Preserve explored-tile memory semantics.

5. Remove the transitional facade.
   - Delete `IBattleSessionQuery`.
   - Delete the facade-style `BattleSessionQuery` implementation.
   - Remove `BattleSession.Query`.
   - Make sure no controller, test, or executor call site uses facade methods.

6. Update documentation and tests.
   - Keep `docs/battlescape-architecture.md` and `docs/battle-session-command-pattern.md` aligned with the actual code.
   - Rename or update `BattleSessionQueryTest` so it tests concrete query classes instead of facade methods.
   - Add focused tests whenever a new query type is added.

## Design Rules

- Query objects are read-only.
- Query objects may read session internals, board state, unit state, and visibility snapshots.
- Query objects must not raise events.
- Query objects must not call mutation execution.
- Query objects must not mutate `BattleSession`, `BattleBoardState`, `BattleUnitState`, or visibility snapshots.
- Avoid out params.
- Check null-returning and failure-prone helpers before continuing.
- Do not enable nullable for the query API.
- Queries that cannot answer because an input is invalid return `BattleQueryFailureResult<TResult>`.
- Use empty collections for no matching results.
- Use strategy objects inside individual queries only when the algorithm has real variants.

## Acceptance Checks

Before considering the refactor complete:

- `rg "session\\.Query\\b" scripts test scenes` returns no production call sites.
- `rg "IBattleSessionQuery" scripts test scenes` returns no results.
- `rg "#nullable enable" scripts/battle/queries` returns no results.
- `BattleSession` exposes `Queries`, not a growing public method list for tactical questions.
- Existing visibility, movement, availability, and faction-unit tests pass through concrete query objects.
- `dotnet test fun-project.sln` passes.

## Follow-Up Candidates

After the typed query surface is in place:

- Extract a movement-range strategy if `GetPossibleMoveTilesForUnit` becomes too board-scan heavy.
- Extract targeting queries for weapons, thrown items, and abilities.
- Add AI scoring queries that compose existing smaller queries instead of reading session internals directly.
