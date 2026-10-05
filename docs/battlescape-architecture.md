# Battlescape Tactical Runtime Architecture

The tactical runtime is scene-independent C# in `scripts/battle/`. It is the single source of truth for live battle state. Godot scenes read its state and play committed events.

Detail: [battle-action-executor.md](battle-action-executor.md) (execution rules), [battle-session-command-pattern.md](battle-session-command-pattern.md) (typed reads and commands), [battlescape-tile-system.md](battlescape-tile-system.md) (board, LOS, and paths), [battle-map-authoring.md](battle-map-authoring.md) (map authoring).

## Design Goals

- `BattleSession` owns all live tactical state.
- `BattleAction` values are the only gameplay-initiated writes. `BattleActionExecutor` resolves them.
- `BattleRuntime.Query` runs typed `IBattleSessionQuery<TResult>` objects for read-side questions.
- Scenes own presentation, input, and playback. They read through queries and react to committed events.
- Board coordinates are the engine-agnostic `FunProject.Core.Vector3I` (X = width, Y = levels, Z = depth). Authored resources use Godot's `Vector3I`. The runtime boundary converts.

## Runtime Diagram

```mermaid
flowchart LR
    subgraph Authored["Authored Godot resources"]
        BattleType["BattleTypeData\nmap pool of BattleMap scenes"]
        ActorData["CombatantData / FactionData\nitem data"]
    end

    subgraph Runtime["Battle runtime (scene-independent C#)"]
        Facade["BattleRuntime\nQuery, ExecuteAction,\nproof creation, hook registration"]
        Executor["BattleActionExecutor\nhook registry, action stack,\ninterrupt capture"]
        Session["BattleSession\nturns, availability, visibility,\nevent dispatch"]
        Board["BattleBoardState\ntiles, occupancy,\nBFS pathfinding"]
        Queries["IBattleSessionQuery&lt;TResult&gt;\ntyped read questions"]
        Setup["BattleSetup\nresolved launch description"]
        Factory["BattleFactory\nstartup entry point"]
    end

    subgraph Presentation["Godot presentation"]
        Launcher["BattleLauncher\nstandalone host"]
        Scene["BattleScene\nhost, refresh wiring"]
        Ui["BattleUiController\nselection / targeting FSM"]
        Input["BattleInputController\ntile picking"]
        View["BattleUI\nauthored controls"]
        Director["EventPlaybackDirector\nevent playback"]
    end

    BattleType -->|"BattleSetupResolver.Resolve"| Setup
    Setup -->|"BattleFactory.Start"| Factory
    Factory -->|"builds session + runtime"| Facade
    ActorData --> Session
    Facade -->|"ExecuteAction"| Executor
    Executor -->|"primitives commit"| Session
    Session --- Board
    Facade -->|"Query"| Queries
    Queries -->|"read only"| Session
    Queries -->|"read only"| Board

    Launcher -->|"resolve type, Start(setup), Present"| Scene
    Input -->|"tile events"| Ui
    View -->|"intents"| Ui
    Ui -->|"build + submit actions"| Facade
    Scene -->|"refresh view models"| View
    Facade -->|"ActionCompleted"| Director
    Director -->|"PlaybackIdle"| Scene
```

## Ownership Rules

- `BattleSession` owns: completion (`Option<BattleOutcome>`), turn number, active side, faction order, unit pool, and alive/dead storage. It also owns the round queue, current-turn availability, per-faction visibility and explored memory, capture membership, and event emission. The UI owns unit selection.
- `BattleBoardState` owns: tile storage, walkability and occupancy, adjacency, occupant placement, movement, removal, and board-local pathfinding.
- `BattleAction` owns action-specific legality checks and application. A composite action unfolds into primitives.
- `BattleActionExecutor` owns: the hook registry (default systems register in its constructor), `RegisterHook`/`UnregisterHook`/`Dispose`, the pending action stack, invocation order, and interrupt capture. A failed submission clears queued work and propagates the exception. Committed state and events remain.
- `BattleRuntime` owns: the public methods `Query` and `ExecuteAction`, the proof methods `TryGetAlive`/`TryGetAliveObject`/`TryGetAttackTarget`/`TryGetTile`, the `RegisterHook`/`UnregisterHook` facade, and the session's single executor. It re-raises `BattleEventCommitted` and raises `ActionStarted`/`ActionCompleted`.
- `IBattleSessionQuery<TResult>` implementations each own one question and its result shape.
- Unit inventory belongs to `BattleUnitState`.
- `BattleLauncher` resolves a `BattleTypeData`, starts the runtime, loads `BattleScene.tscn`, and calls `Present`. `BattleScene` holds the runtime and setup and instances the pooled `BattleMap`. It builds `EventPlaybackDirector`, `BattleUnitMeshes`, `BattleUiController`, and `BattleInputController`. It pushes view models into the authored `BattleUI` view.
- `BattleUiController` owns the selection and targeting FSM and turns intents into queries and submitted actions. `BattleInputController` raycasts tiles and emits `TileClicked`/`TileHovered`/`ConfirmPressed`/`CancelPressed`. `EventPlaybackDirector` subscribes to `ActionCompleted`, plays each result's committed events in commit order (coalescing consecutive move events), and raises `PlaybackIdle`. They use runtime queries and submitted actions for tactical reads and writes.

## Runtime Components

### Battle startup

`BattleFactory` is the production entry point. `Start(type, seed?, playerDeployment?)` resolves the setup and starts it. `Start(setup, hitChance?)` is the single startup routine:

1. `BattleSetupResolver.Resolve` picks a map from `BattleTypeData.MapPool` with the seed. It probes the chosen `BattleMap` scene for its `MapData`. It builds ordered sides with positioned units. Resolver failures are typed `BattleSetupFailure`s: `EmptyMapPool`, `MapSceneInvalid`, `UnknownFaction`, deployment faction mismatches, and `SpawnSlotShortfall`.
2. `Start(setup)` creates a fresh board from the map. It then parses and validates the complete layout before any side effect. The layout check covers objectives, a player faction outside the sides, and duplicate, unavailable, or overlapping unit/object cells.
3. The complete roster and objects materialize on the board, and the objectives instantiate. The factory then constructs the session with its scheduler already valid. The initial round queue, active side, and availability come from the real roster. A roster without an eligible first faction is a construction failure. The factory adds the runtime and its default hooks.
4. Declared systems register through the narrow `BattleHookRegistration` capability. The capability carries no runtime handle, queries, or submission. The single private factory-owned bootstrap runs after registration.
5. The bootstrap announces the initial placements, `SessionStarted`, and the first turn through the ordinary event dispatch. Every placement announcement precedes the initial spotting announcements. Hooks see the complete roster and availability. Session start precedes the first turn and its AP refresh while the battle is ongoing. Interrupts run only after the startup primitive. The factory owns all setup.
6. An initialization fault before the runtime exists throws before anything registers. A fault after construction disposes the runtime and rethrows the original exception. A start whose terminal objective ends the battle during startup succeeds and returns the completed runtime.

A supplied `PlayerDeployment` keeps its exact campaign faction and combatant references. `Combatant.OwningFaction` stays unchanged. A mismatch is a typed failure. The campaign-to-battle handoff is deferred. `BattleLauncher` starts the standalone battle and presents `BattleScene` the same way.

### BattleSession

The session is turn scheduling, state, and event dispatch. It owns unit ids, alive/dead moves, faction elimination, and the round queue. It also owns action-point (AP) availability for the active side, visibility refresh, and the draw resolution at turn end. Its scheduler establishes the initial round queue, the active side (the first faction with a conscious unit), and availability at construction. A roster with no conscious faction fails construction.

Read battle state through typed queries via `runtime.Query(...)`. Actions and queries use internal session helpers. Scenes and AI depend on the runtime's public surface.

Event dispatch processes a guarded queue. Each broadcast first refreshes visibility and invalidates affected action-option availability, then invokes `BattleEventCommitted`. An event raised mid-dispatch waits until the current event finishes (breadth-first). A throwing hook clears the queue and reports the exception. The session announces. The executor reacts.

### Life, incapacity, and conscious forces

- `IsAlive` is `CurrentHealth > 0`. `IsDead` is zero health.
- `IsUnconscious` is `IsAlive && CurrentStun >= CurrentHealth`.
- `IsIncapacitated` is `IsDead || IsUnconscious || IsImmobilized`.
- Acting requires positive AP and a capable actor, plus the session's turn and availability checks (`CanUnitActNow`).

`AliveUnit` proves life and board presence, including unconscious bodies. Bodies stay in alive storage on their tile, block occupancy, and remain targetable. Damage actions can affect them. Faction elimination and scheduling count living conscious units, independent of AP or temporary immobilization. Health-based survivor summaries keep unconscious participants alive.

### Damage, disabling, and recovery

`Damage` packets carry amount, element, and `DamageKind` (default `Health`). `DamageResolver` (`scripts/battle/combat/`) resolves them in bundle order inside `ApplyDamageTo`:

- Health packets consume remaining armor. An element match removes 1.5× armor (floored). Health spill uses the original packet amount.
- Stun packets bypass armor, leave it for later packets, and increase only `CurrentStun`.
- Non-positive packets resolve to zero.

`UnitDamagedBattleEvent` carries `ArmorDamage`, `HealthDamage`, and `StunDamage` plus the pre-mitigation bundle. Death emits `UnitKilledBattleEvent`. A new unconscious transition emits `UnitUnconsciousBattleEvent`. Other hits emit `UnitDamagedBattleEvent`.

A lethal bundle emits only the kill event, including death after earlier unconsciousness. Kill credit is death-only. Elimination objectives observe both event types. Death playback and kill-only hooks keep consuming `UnitKilledBattleEvent`.

Status effects (`StatusEffectSpecData` in `scripts/items/effects/`) apply after survival. Each packet uses its own `ApplyChancePercent` and `RequiresHealthDamage`. A health-damage requirement needs positive health spill from that packet. `StatusEffectSystem` ticks at the owner's turn end: decrement, damage-over-time through the normal pipeline, expiry.

Recovery order at the ending faction's turn end: `StatusEffectSystem` and `ArmorRegenSystem` (priority −100), then `StunRecoverySystem` (priority −90). Only armor or health damage restarts the armor regen delay. In-progress battles recover up to `5u` stun per owner turn for living conscious units. A positive reduction raises `UnitStunRecoveredBattleEvent`. Routing max-health debuff effects through the damage pipeline is deferred.

### Board objects

`BattleObjectState` is an immovable placed object. Attackable objects carry `ObjectHealthCapability`. `BattleEntity` carries a unit or object identity. `BattleRuntime.TryGetAttackTarget` creates its `AttackTarget` proof. The proof is `Some` only for a live unit or a live health-bearing object.

Feasibility (weapon, self, ally, liveness, attackability, visibility, range) resolves once through `AttackContext.Resolve`. `AttackEntity` and the `GetHitChanceForAttack` preview share this check, so both callers use the same feasibility rules. Against a unit, the attack rolls through the session's `IHitChanceCalculator` (default `StandardHitChanceCalculator`: Aim − cover, clamped 0–100) with the seeded RNG. Against an object, the attack skips the calculator. A feasible object shot always connects. `weapon.TrySpendShot(attacker.ActiveBuffDamageMods)` spends ammo before the roll; a missed shot consumes ammunition.

Object damage goes through `ApplyDamageTo`'s object overload. Health packets reduce the capability. The overload ignores stun and status payloads. Zero health is the terminal `Destroyed` transition. Occupancy clears before the broadcast. Objects emit `ObjectDamagedBattleEvent` and `ObjectDestroyedBattleEvent`.

Timed objects carry `TimedEffectCapability`. `SpecialObjectTimerSystem` expires them at turn end and resolves payloads before `ObjectExpiredBattleEvent`. `TimedObjectObjective` fails the mission when a tracked timed object expires or is destroyed.

Area effects resolve through `CapabilityEffectResolver`. A blast hits every unit and every live health-bearing object in the radius. Friendly fire is included. The blast uses the ordinary damage pipeline.

### Capture summary and campaign lifetime

The first `EndBattle` freezes capture membership before broadcasting `SessionEndedBattleEvent`. Victory with a designated player captures every living unconscious enemy. The rule covers every cause of the unconscious state.

The list is read-only and deduplicates original `Combatant` references. The Combatants keep their identity and faction. Roster membership stays unchanged. `GetFactionEndOfBattleSummary` exposes `CapturedEnemies` only for that player's victory summary. Unconscious player participants remain survivors.

`GameState.Captivity` retains added Combatants by reference for the campaign's lifetime, deduplicates by reference, and returns detached snapshots. Standalone battle capture summaries work. Captivity transfer and the campaign-to-battle handoff are deferred.

### IBattleSessionQuery

```csharp
public interface IBattleSessionQuery<out TResult>
{
  public TResult Execute(BattleSession session);
}
```

The result shape is part of the contract:

- Bare `TResult` — the question always has an answer, usually because proof-typed inputs (`AliveUnit`, `ValidatedPoint`) carry the invariants. `CanUnitActNow` takes a raw `BattleUnitState` because `false` is a valid answer for any unit, including after completion.
- `Option<T>` — absence is normal (`GetUnitAtTile`, `GetObjectivesForFaction`).
- `Either<BattleQueryFailure, TResult>` — fallible (`GetHitChanceForAttack` for an out-of-range or unseen target, `GetFactionEndOfBattleSummary` before the battle ends).

Scene code creates proofs at the runtime boundary: `TryGetAlive(unit)`, `TryGetAliveObject(obj)`, `TryGetAttackTarget(entity)`, and `TryGetTile(coordinates)`. Missing or dead units and off-board tiles return `None` as expected absence at the proof boundary. Passing a proof into an invalid context is a caller bug. Let the guarding exception propagate, per the trusted-core convention. A `TryGetTile` proof remains valid because board bounds stay fixed.

Examples: `FindPathForUnit`, `GetPossibleMoveTilesForUnit`, `GetVisibleEnemiesForUnit`, `CanUnitActNow`, `GetHitChanceForAttack`, `GetAvailableActionsForUnit`, `GetPlayerFactionQuery`, `GetFactionEndOfBattleSummary`. Use one question per query. Keep the algorithm in that query's implementation. Keep query classes single-purpose, without enum modes, nullable selector fields, or behavior controlled by unrelated properties. Put a complex or variable algorithm in a strategy behind that specific query. Keep each `BattleRuntime.Query` call mapped to one query implementation.

### BattleBoardState

The board stores a flat materialized `BattleTileState[]`, unit and object occupancy, and positions. Cells use the index `x + width * (z + depth * y)` (X width, Y height, Z depth). Adjacency is eight-way: orthogonal plus same-level diagonals. A diagonal step needs at least one open flank. Pathfinding is a pure-C# breadth-first search with a fixed direction order; the board exposes it as `FindPath`. Unit-specific questions stay in queries (`FindPathForUnit`, `GetPossibleMoveTilesForUnit`). See [battlescape-tile-system.md](battlescape-tile-system.md).

### Visibility

The session owns visibility because it derives from unit positions, tile state, and explored memory:

- `VisionStat` is the sight range of each living conscious observer.
- Visible tiles come from scanning a 3D bounding box within Euclidean range and raytracing line of sight per candidate. Whole-tile `BlocksLineOfSight` occludes. A diagonal between two blockers passes only when at least one flanking tile is open. Tiles with `BlocksVerticalLineOfSight` block vertical passages.
- A unit is visible when it stands on a visible tile. Each unit stores its visible tiles and visible units; each faction stores explored-tile memory.
- A faction always knows its own living units. Conscious observers contribute faction vision and can see unconscious bodies. Exploration persists.
- After each committed event, only units whose cell or consciousness changed recompute visible tiles. Every conscious observer rebuilds visible-unit membership wholesale.

Vision is omnidirectional. Smoke attenuation, lighting-dependent vision, and last-known enemy memory remain deferred.

### BattleAction and the executor

The built-in actions are `SpawnUnit`, `MoveUnit` (composite), `InteractWithObject`, `AttackEntity`, `ReloadWeapon`, `ThrowItem`, `UseItem`, `ApplyDamage`, `PassUnit`, and `EndFactionTurn`.

```csharp
var result = runtime.ExecuteAction(BattleAction.MoveUnit(unit, [destination]));
```

`Submit` and the `ExecuteAction` facade resolve the chain first. They return one `BattleActionExecResult`. The result holds the submitted action plus every event committed while resolving it. The span is a zero-copy `ReadOnlySpan<BattleEvent>` into the executor's append-only log. An interrupted action or a battle that ends mid-submission leaves the remaining steps unexecuted.

`Submit` converts only a `Result.Rejected` step to `InvalidOperationException` because parameters are trusted. The executor clears queued work first. Arbitrary action or hook exceptions then propagate unchanged. Committed state and events remain. Full rules: [battle-action-executor.md](battle-action-executor.md).

## Representative Flow: Move Command

```mermaid
sequenceDiagram
    autonumber
    actor Player
    participant Input as BattleInputController
    participant Ui as BattleUiController
    participant Queries as BattleRuntime.Query
    participant Runtime as BattleRuntime
    participant Exec as BattleActionExecutor
    participant Action as MoveUnit
    participant Session as BattleSession
    participant Board as BattleBoardState
    participant Director as EventPlaybackDirector
    participant Scene as BattleScene
    participant View as BattleUI

    Player->>Input: Clicks a reachable tile
    Input->>Ui: TileClicked(tile)
    Ui->>Queries: Preview path (AliveUnit proof)
    Queries->>Board: FindPath
    Queries-->>Ui: Preview path
    Player->>Ui: Confirms the move
    Ui->>Runtime: ExecuteAction(MoveUnit with destination steps)
    Runtime->>Exec: Submit(action)
    loop Until the action and its reactions settle
        Exec->>Action: Execute the next primitive step
        Action->>Session: First Execute only: ValidateRoute once (total AP, adjacency, occupancy)
        Action->>Session: Every Execute: check liveness, incapacity, next-cell occupancy
        Action->>Board: Commit the next step
        Action-->>Session: Raise UnitMoved, TileOccupied
        Session->>Exec: Broadcast, then fire hooks post-broadcast
        Exec->>Exec: Collect hook interrupt actions for the committed step
    end
    Exec-->>Runtime: BattleActionExecResult (action + committed events)
    Runtime->>Director: ActionCompleted(result)
    Runtime-->>Ui: Returns the result
    Director-->>Scene: PlaybackIdle after the coalesced move run animates
    Scene-->>View: Refresh view models from queries
```

## Turn Flow Notes

- The factory constructs the session from the complete initial roster. Its scheduler establishes the first round queue, the active side, and availability at construction. The factory-owned bootstrap announces the placements, `SessionStarted`, and the first turn through the ordinary event machinery. `SpawnUnit` is the in-game reinforcement operation.
- The scheduler skips a declared faction with no conscious unit when it selects the active side. A roster with no conscious faction at all fails construction.
- `ActiveSide` identity is a scheduler-owned fact the session exposes. The session owns `TurnNumber`.
- `EndFactionTurn` is the explicit faction-turn action.
- Turn start follows the same dispatch-then-refresh shape at both invocation sites (the factory bootstrap's first turn and subsequent side turns). Both sites raise the turn-start events (`TurnStarted`, plus `ActiveSideChanged` on a side flip or `SessionStarted` on battle start). Then they refresh action points with `RefreshForNewTurn`.
  - The factory establishes the first turn's scheduling state (round queue, active side, availability) at construction. Each subsequent side turn runs one scheduler `Advance()` after the turn-end dispatch and its outcome check. Exhaustion clears availability and resolves the draw. A reported new round increments `TurnNumber` once.
  - `ActiveSideChanged`/`TurnStarted` observers see the new active side with its availability already established and the round number already applied.
  - The initial AP refresh covers all living units. Later refreshes cover only the turning faction's living units. Both sites skip the AP refresh when the dispatch already decided the battle.
  - Buff evaluation is an ordinary hook on `TurnStartedBattleEvent` (`TurnStartBuffHook`) and on `UnitAddedBattleEvent` at spawn (`UnitSpawnedBuffHook`). The executor's constructor registers both hooks. Dispatch fires them after the event's broadcast. A buff flip applies during the dispatch. For turn start, the flip applies before the AP refresh. That refresh runs once the dispatch completes and reads (possibly buffed) `MaxActionPoints`.
  - Mid-dispatch observers of `SessionStarted`/`TurnStarted` see pre-refresh action points. This is deliberate. When the battle is still ongoing, the refresh always completes before the submission returns.
- `PassUnit` ends a unit activation and currently advances the turn automatically if that side has no remaining actable units.
- Unit death updates alive/dead storage, board occupancy, current-turn availability, and faction queue membership through session bookkeeping. Unconsciousness consumes availability and removes the faction from future turns when no conscious ally remains, while preserving the body and its occupancy. A disabled active faction keeps its current queue head until explicit turn end.
- The designated player's loss of all conscious units ends the battle as defeat. `ObjectiveSystem` suppresses directives on that death or unconscious event, so the player-wipe defeat rule keeps priority over objective results. In a battle with no designated player, the loss of all living conscious units stays in progress until turn end resolves a draw.

## Battle Events

Events are records rooted at `BattleEventTag`. Hook keys are event types or shared marker interfaces (`IUnitBattleEvent`, `IPositionedBattleEvent`, `ICausedByUnit`). Presentation observes `BattleSession.BattleEventCommitted` (or the runtime re-raise), which fires only after the state change committed.

- Session and turns: `SessionStarted`, `SessionEnded`, `TurnStarted`, `TurnEnded`, `ActiveSideChanged`.
- Units: `UnitAdded`, `UnitActivationEnded`, `UnitMoved`, `TileOccupied`, `UnitAttacked`, `UnitReloadedWeapon`, `UnitDamaged`, `UnitKilled`, `UnitUnconscious`, `UnitStunRecovered`, `UnitArmorRegenerated`, `UnitSpotted`.
- Statuses and buffs: `UnitStatusEffectApplied`, `UnitStatusEffectTicked`, `UnitStatusEffectExpired`, `UnitBuffActivated`, `UnitBuffDeactivated`.
- Items: `ItemThrown`, `ItemUsed`, `CapabilityResolved`.
- Objects: `ObjectPlaced`, `ObjectInteracted`, `ObjectExpired`, `ObjectDamaged`, `ObjectDestroyed`.
- Objectives: `ObjectiveAdded`, `ObjectiveCompleted`, `ObjectiveFailed`.

Hooks register on the executor and fire once per committed event, after the broadcast. Default systems:

- status effects and armor regen (`TurnEndedBattleEvent`, priority −100);
- stun recovery (−90);
- capability effects (`ItemThrownBattleEvent`);
- buff evaluation (`TurnStartedBattleEvent` and `UnitAddedBattleEvent`);
- `ObjectiveSystem` (catch-all `BattleEventTag` router at +100, filtering each objective's declared keys).

Registration uses event-tag types.

## Deferred Extensions

`BattleRules`, a battle AI controller, movement- and targeting-query strategies, and reaction fire are still future work. When they arrive, they must read through typed queries, commit through actions, and emit events for presentation.

## Canonical Vocabulary

`BattleSession`, `BattleAction`, `BattleActionExecResult`, `BattleActionExecutor`, `BattleHook` (`BattleHook<TEvent>`, `HookContext`), `IBattleSessionQuery<TResult>`, `BattleQueryFailure`, `BattleRuntime`, `Either<BattleQueryFailure, TResult>`, `BattleEventTag`, `BattleBoardState`, `BattleTileState`, `BattleUnitState`, `BattleEvent`, `BattleScene`, `BattleUiController`, `EventPlaybackDirector`.
