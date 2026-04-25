# Battlescape Tactical Runtime Architecture

This document describes the tactical runtime shape in the repo and the nearby extensions it is designed to support. It reflects the current command-based session design and typed read-side query interface.

## Design Goals

- `BattleSession` is the single source of truth for live tactical state.
- `BattleSession` is constructed from battle setup data: a prepared board state, stable faction order, and faction rosters.
- `BattleSessionMutation` is the authoritative command layer.
- `BattleActionExecutor` validates and invokes queued `BattleSessionMutation` values.
- Read-side battle questions are represented as typed query objects, executed through a single query runner.
- Controllers, HUD code, and AI should ask battle-state questions through explicit query types instead of accumulating public query methods on `BattleSession`.
- Godot scene nodes own presentation, input, and focused-unit UX. They do not own tactical truth.
- Board coordinates use normal `Godot.Vector3I` semantics:
  - `X` = width
  - `Y` = levels / height
  - `Z` = length / depth
- Future tactical systems such as pathfinding, effects, and AI should integrate through explicit session reads and mutation execution instead of mutating scene state directly.

## Current Runtime Diagram

```mermaid
flowchart LR
    subgraph Templates["Static Definitions (Godot Resources)"]
        CombatantData["CombatantData"]
        WeaponData["WeaponData / FirearmWeaponData"]
        ItemData["EquippableItemData / ThrowableItemData"]
        FactionData["FactionData"]
        BattleConfig["Battle setup data"]
    end

    subgraph Runtime["Authoritative Tactical Runtime"]
        BattleSession["BattleSession\nsingle source of truth\nturn flow + bookkeeping"]
        BoardState["BattleBoardState\nBattleTileState[x,y,z]\noccupancy + spatial path queries"]
        UnitState["BattleUnitState[]\nposition, AP, health,\ninventory refs, equipped weapon"]
        VisibilityState["BattleVisibilitySnapshot\nper-faction fog of war\nexplored tiles + visible enemies"]
        VisibilitySystem["BattleVisibilitySystem\ntile-based LOS + faction FOV rebuilds"]
        EventStream["BattleEvent stream"]
        Mutations["BattleSessionMutation\nexecutable commands"]
        ActionExecutor["BattleActionExecutor\nmutation queue + invocation"]
        Queries["BattleSessionQuery<TResult>\ntyped read questions"]
        QueryRunner["BattleQueryRunner\nread-query invocation"]
    end

    subgraph Presentation["Godot Presentation / Input"]
        SceneController["BattleSceneController\ninput translation,\nselection UX, camera"]
        BattleScene["BattleScene / scene nodes\nmap visuals, units, VFX"]
        HUD["Battle HUD\naction buttons, turn controls,\npreviews"]
    end

    Templates --> BattleSession

    BattleSession --- BoardState
    BattleSession --- UnitState
    BattleSession --- VisibilityState
    BattleSession --- EventStream
    VisibilitySystem -->|"rebuild"| VisibilityState
    Mutations -->|"refresh on success"| VisibilitySystem
    Queries -->|"read only"| BattleSession
    Queries -->|"read only"| BoardState
    Queries -->|"read only"| VisibilityState

    HUD -->|"player request"| SceneController
    BattleScene -->|"selection / hover / click"| SceneController
    SceneController -->|"translate to mutation"| ActionExecutor
    SceneController -->|"ask typed query"| QueryRunner

    ActionExecutor -->|"invoke"| Mutations
    Mutations -->|"execute against"| BattleSession
    QueryRunner -->|"invoke"| Queries

    EventStream --> BattleScene
    EventStream --> HUD
```

## Ownership Rules

- `BattleSession` owns:
  - battle phase
  - turn number
  - active side
  - global faction order
  - round queue
  - alive and dead unit bookkeeping
  - current-turn unit availability
  - faction visibility and explored-tile state
  - authoritative battle event emission
- `BattleBoardState` owns:
  - tile storage
  - walkability and occupancy state
  - adjacency queries
  - occupant placement, movement, and removal
  - board-local pathfinding queries
- `BattleSessionMutation` owns mutation-specific application and orchestration after executor-side validation succeeds.
- `BattleVisibilitySystem` owns:
  - tile-based line-of-sight checks
  - tile visibility checks
  - deriving visible units from visible tiles
  - rebuilding faction fog-of-war snapshots from session state
- `BattleActionExecutor` owns:
  - preview validation for supported mutations
  - the pending mutation queue
  - invocation order
  - last-result tracking
  - exception isolation around command execution
- `BattleQueryRunner` owns:
  - the single public entry point for read-side tactical questions
  - null checks on submitted query objects
  - invoking typed query objects against the current session
- concrete `BattleSessionQuery<TResult>` classes own:
  - one specific read-side question
  - the result type and failure semantics for that question
  - any query-specific composition across session, board, visibility, or unit state
- `BattleSceneController`, `BattleScene`, and HUD own:
  - focused / selected unit UX
  - previews
  - camera behavior
  - presentation timing
- `BattleSession` does not track a selected unit. Selection is presentation state.
- `BattleSession` should not grow a public method for every controller, HUD, or AI question.
- Inventory currently lives on `BattleUnitState`. There is no separate `BattleItemState` runtime layer yet.

## Runtime Components

### BattleSession

`BattleSession` currently exposes authoritative state and bookkeeping around:

- `Board`
- `Phase`
- `TurnNumber`
- `ActiveSide`
- `AliveUnits`
- `DeadUnits`
- `GlobalFactionTurnOrder`
- `TurnQueue`
- `FactionRosters`

It is initialized with:

- `BattleBoardState board`
- `IEnumerable<Faction> globalFactionOrder`
- `IDictionary<Faction, IEnumerable<Combatant>> factionRosters`

The configured faction order must contain at least one faction after rosters are included. The setup turn queue and `ActiveSide` are initialized from the first faction in that order.

The session owns:

- creating runtime unit ids
- moving units between alive and dead storage
- handling faction elimination
- rebuilding and advancing the round queue
- refreshing action-point availability for the active side
- rebuilding faction visibility after successful mutations
- ending the battle when no living factions remain

The target public read-side surface is a query runner, not a growing method list:

```csharp
var result = session.Queries.Execute(new SomeBattleQuery(...));
```

The session may keep internal helpers for commands and query objects, but controllers and AI should not depend on those helpers directly.

### BattleSessionQuery

Read-side tactical questions should be modeled as typed query objects.

Current base shape:

```csharp
public abstract class BattleSessionQuery<TResult>
{
  public string QueryId { get; }

  protected BattleSessionQuery(string queryId)
  {
    if (string.IsNullOrWhiteSpace(queryId))
      throw new ArgumentException("Query id cannot be null or whitespace.", nameof(queryId));

    QueryId = queryId;
  }

  internal abstract BattleQueryResult<TResult> Execute(BattleSession session);
}
```

Current runner shape:

```csharp
public sealed class BattleQueryRunner
{
  private readonly BattleSession _session;

  internal BattleQueryRunner(BattleSession session)
  {
    _session = session ?? throw new ArgumentNullException(nameof(session));
  }

  public BattleQueryResult<TResult> Execute<TResult>(BattleSessionQuery<TResult> query)
  {
    ArgumentNullException.ThrowIfNull(query);
    return query.Execute(_session);
  }
}
```

`BattleQueryResult<TResult>` has explicit success and failure shapes. Query callers should handle `BattleQueryFailureResult<TResult>` before using a `BattleQuerySuccess<TResult>.Value`; missing units, invalid tiles, and invalid battle-state questions are failures, not nullable query values.

Example query types:

- `FindPathForUnit`
- `GetPossibleMoveTilesForUnit`
- `GetVisibleEnemiesForUnit`
- `GetVisibleUnitsForFaction`
- `CanUnitActNow`
- `IsTileVisibleToFaction`
- `GetFactionAliveUnits`

Each query should encode one question. Avoid catch-all query classes with enum modes, nullable selector fields, or behavior controlled by unrelated properties. If a query algorithm becomes complex or variable, use a strategy behind that specific query rather than turning the query runner into a dispatch switch.

### BattleBoardState

`BattleBoardState` now owns board-local spatial operations:

- `TryPlaceOccupant(...)`
- `TryMoveOccupant(...)`
- `TryClearOccupant(...)`
- `FindPath(...)`
- `IsAdjacent(...)`

Pathfinding is currently integrated directly into the board through Godot `AStar3D`. Query objects should be the controller-facing surface for unit-specific path questions, for example `FindPathForUnit` and `GetPossibleMoveTilesForUnit`. If path rules become substantially more unit-specific later, this can be extracted behind a movement-query strategy without changing the controller-facing query contract.

### BattleVisibilitySystem

`BattleVisibilitySystem` is now a first-class runtime subsystem.

Current behavior:

- uses `VisionStat` as the maximum sight range per unit
- traces LOS from tile center to tile center
- treats units as visible when they stand on a currently visible tile
- builds per-faction current visibility and explored-tile memory
- keeps own living units known to their faction even without direct LOS
- rebuilds the full visibility snapshot after every successful battle mutation

Current limitations:

- vision is omnidirectional
- `BlocksLineOfSight` is whole-tile occlusion
- there is no smoke attenuation, lighting model, or last-known enemy memory yet

### BattleSessionMutation

The current built-in authoritative mutations are:

- `StartBattle`
- `SpawnUnit`
- `MoveUnitStep`
- `MoveUnit`
- `ThrowItem`
- `ApplyDamage`
- `PassUnit`
- `EndFactionTurn`

Each mutation executes directly through:

```csharp
var result = mutation.Execute(session);
```

Mutation results are returned as `BattleMutationResult`, including:

- success or failure
- failure reason
- optional affected unit
- optional message

### BattleActionExecutor

`BattleActionExecutor` currently queues `BattleSessionMutation`.

Current responsibilities:

- `Evaluate(...)`
- `ExecuteNow(...)`
- `Enqueue(...)`
- `EnqueueRange(...)`
- `Tick()`
- `DrainQueue(...)`
- `MutationStarted` event
- `MutationResolved` event
- `LastResult`
- `ActiveMutation`

The executor does not currently:

- own selection logic
- mutate session state directly outside mutation execution

## Representative Flow: Current Move Command

```mermaid
sequenceDiagram
    autonumber
    actor Player
    participant HUD as Battle HUD
    participant Controller as BattleSceneController
    participant Queries as BattleQueryRunner
    participant Exec as BattleActionExecutor
    participant Mutation as MoveUnit
    participant Session as BattleSession
    participant Board as BattleBoardState
    participant Event as BattleEvent stream
    participant View as BattleScene

    Player->>HUD: Confirm move
    HUD->>Controller: Move request
    Controller->>Queries: Execute FindPathForUnit query
    Queries->>Session: Read unit state
    Queries->>Board: FindPath
    Queries-->>Controller: Return path
    Controller->>Exec: Evaluate move mutation
    Exec->>Session: Validate active side, AP, unit availability
    Exec->>Board: Validate adjacency, occupancy, and path legality
    Controller->>Exec: Enqueue move mutation
    Exec->>Mutation: ExecuteUnchecked(session)
    Mutation->>Board: Commit each provided board step
    Mutation->>Event: Raise UnitMoved per step
    Exec-->>Controller: Return BattleMutationResult
    Event-->>View: Animate movement
    Event-->>HUD: Refresh AP and prompts
```

## Turn Flow Notes

- The battle starts from `BattlePhase.Setup` and transitions to `InProgress` through `StartBattle`.
- Setup initializes the turn queue from the stable global faction order.
- `StartBattle` rebuilds the active round queue from living factions in that order.
- `ActiveSide` and `TurnNumber` are owned by the session.
- `EndFactionTurn` is the explicit faction-turn mutation.
- `PassUnit` ends a unit activation and currently advances the turn automatically if that side has no remaining actable units.
- Unit death updates alive/dead storage, board occupancy, current-turn availability, and faction queue membership through session bookkeeping.

## Current Battle Events

The current event stream is intentionally small and authoritative:

- `SessionStarted`
- `SessionEnded`
- `TurnStarted`
- `TurnEnded`
- `ActiveSideChanged`
- `UnitAdded`
- `UnitActivationEnded`
- `UnitMoved`
- `UnitDamaged`
- `UnitKilled`
- `ItemThrown`

Presentation code should react to these events instead of inferring state changes from executor internals.

## Future Extensions

These systems are still part of the intended architecture, but they are not implemented as first-class runtime systems yet:

- `BattleRules`
- `BattleEffectSystem`
- `BattleAIController`
- movement-query strategies
- targeting-query strategies

When they are added, they should follow these rules:

- read battle state through typed query objects instead of duplicating tactical truth
- commit tactical changes through explicit session helpers and mutation execution
- emit structured battle events instead of mutating scene nodes directly
- keep presentation-only concerns out of the authoritative runtime

Step-based movement, interrupts, reaction fire, and richer combat-effect hooks are still future extensions on top of the current command-based runtime.

## Canonical Vocabulary

Use these names consistently in future tactical work:

- `BattleSession`
- `BattleSessionMutation`
- `BattleMutationResult`
- `BattleActionExecutor`
- `BattleActionEvaluation`
- `BattleSessionQuery<TResult>`
- `BattleQueryRunner`
- `BattleQueryResult<TResult>`
- `BattleQueryFailure`
- `BattleBoardState`
- `BattleTileState`
- `BattleUnitState`
- `BattleEvent`
- `BattleSceneController`

Detailed design notes:

- [BattleActionExecutor Design](./battle-action-executor.md)
- [BattleSession Command Pattern](./battle-session-command-pattern.md)
- [Battle Query Interface Implementation Plan](./battle-query-interface-implementation-plan.md)
- [Battlescape Tile System Design](./battlescape-tile-system.md)
