# Battlescape Tactical Runtime Architecture

The tactical runtime is scene-independent C# in `scripts/battle/`. It is the single source of truth for live battle state. Godot scenes observe it and never own tactical truth.

Detail: [battle-action-executor.md](battle-action-executor.md) (execution rules), [battle-session-command-pattern.md](battle-session-command-pattern.md) (typed reads and commands), [battlescape-tile-system.md](battlescape-tile-system.md) (board, LOS, and paths), [battle-map-authoring.md](battle-map-authoring.md) (map authoring).

## Design Goals

- `BattleSession` owns all live tactical state.
- `BattleAction` values are the only gameplay-initiated writes; `BattleActionExecutor` resolves them.
- Read-side questions are typed `IBattleSessionQuery<TResult>` objects run through `BattleRuntime.Query`.
- Scenes own presentation, input, and playback. They read through queries and react to committed events.
- Board coordinates are the engine-agnostic `FunProject.Core.Vector3I` (X = width, Y = levels, Z = depth). Authored resources use Godot's `Vector3I`; the runtime boundary converts.

## Runtime Diagram

```mermaid
flowchart LR
    subgraph Authored["Authored Godot resources"]
        BattleType["BattleTypeData\nmap pool of BattleMap scenes"]
        ActorData["CombatantData / FactionData\nitem data"]
    end

    subgraph Runtime["Battle runtime (scene-independent C#)"]
        Facade["BattleRuntime\nQuery, ExecuteAction,\nproof mints, hook doors"]
        Executor["BattleActionExecutor\nhook registry, action stack,\ninterrupt capture"]
        Session["BattleSession\nturns, availability, visibility,\nevent dispatch"]
        Board["BattleBoardState\ntiles, occupancy,\nBFS pathfinding"]
        Queries["IBattleSessionQuery&lt;TResult&gt;\ntyped read questions"]
        Setup["BattleSetup\nresolved launch description"]
        Factory["BattleFactory\nstartup front door"]
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

- `BattleSession` owns: battle phase, turn number, active side, faction order, unit pool, alive/dead storage, round queue, current-turn availability, per-faction visibility and explored memory, capture membership, and event emission. It does not track a selected unit; selection is presentation state.
- `BattleBoardState` owns: tile storage, walkability and occupancy, adjacency, occupant placement, movement, removal, and board-local pathfinding.
- `BattleAction` owns action-specific legality checks and application. A composite action unfolds into primitives.
- `BattleActionExecutor` owns: the hook registry (default systems register in its constructor), `RegisterHook`/`UnregisterHook`/`Dispose`, the pending action stack, invocation order, and interrupt capture. A failed submission clears queued work and propagates the exception; committed state and events are not rolled back.
- `BattleRuntime` owns: the public doors `Query` and `ExecuteAction`, the proof mints `TryGetAlive`/`TryGetAliveObject`/`TryGetAttackTarget`/`TryGetTile`, the `RegisterHook`/`UnregisterHook` facade, and the session's single executor. It re-raises `BattleEventCommitted` and raises `ActionStarted`/`ActionCompleted`.
- `IBattleSessionQuery<TResult>` implementations each own one question and its result shape.
- Unit inventory lives on `BattleUnitState`; there is no separate battle item runtime layer yet.
- `BattleLauncher` resolves a `BattleTypeData`, starts the runtime, loads `BattleScene.tscn`, and calls `Present`. `BattleScene` holds the runtime and setup, instances the pooled `BattleMap`, builds `EventPlaybackDirector`, `BattleUnitMeshes`, `BattleUiController`, and `BattleInputController`, and pushes view models into the authored `BattleUI` view.
- `BattleUiController` owns the selection and targeting FSM and turns intents into queries and submitted actions. `BattleInputController` raycasts tiles and emits `TileClicked`/`TileHovered`/`ConfirmPressed`/`CancelPressed`. `EventPlaybackDirector` subscribes to `ActionCompleted`, plays each result's committed events in commit order (coalescing consecutive move events), and raises `PlaybackIdle`. None of these validate legality, mutate the session, or cache tactical state.

## Runtime Components

### Battle startup

`BattleFactory` is the production front door. `Start(type, seed?, playerDeployment?)` resolves the setup and starts it; `Start(setup, hitChance?)` is the single startup routine:

1. `BattleSetupResolver.Resolve` picks a map from `BattleTypeData.MapPool` with the seed, probes the chosen `BattleMap` scene for its `MapData`, and builds ordered sides with positioned units. Resolver failures are typed `BattleSetupFailure`s: `EmptyMapPool`, `MapSceneInvalid`, `UnknownFaction`, deployment faction mismatches, and `SpawnSlotShortfall`.
2. `Start(setup)` creates a fresh board from the map, then validates the complete layout — missing objectives, a player faction outside the sides, and duplicate, unavailable, or overlapping unit/object cells — before any runtime construction.
3. The session, objectives, runtime, and default hooks are created. Declared systems register before any unit spawns, so they observe spawning, session start, and the first turn.
4. Spawn, object-placement, and `StartBattle` actions execute. Any fault disposes the runtime and rethrows; no partial runtime escapes.

A supplied `PlayerDeployment` keeps its exact campaign faction and combatant references. Startup never reassigns `Combatant.OwningFaction`; a mismatch is a typed failure. A start whose terminal objective ends the battle during startup still succeeds. The campaign-to-battle handoff is deferred; `BattleLauncher` boots the standalone battle and presents `BattleScene` the same way.

### BattleSession

The session is turn scheduling, state, and event dispatch. It owns unit ids, alive/dead moves, faction elimination, the round queue, action-point availability for the active side, visibility refresh, and the draw backstop at turn end. The configured faction order needs at least one faction; `ActiveSide` starts at the first faction.

Reads go through typed queries via `runtime.Query(...)`; the session grows no public read-method list. Actions and queries may use internal helpers, but scenes and AI depend only on the public surface.

Event dispatch is a guarded queue drain: each broadcast first refreshes visibility and invalidates affected action-option availability, then invokes `BattleEventCommitted`. An event raised mid-dispatch waits until the current event finishes (breadth-first, not inline). A throwing hook clears the queue and surfaces the exception. The session announces; the executor reacts.

### Life, incapacity, and conscious forces

- `IsAlive` is `CurrentHealth > 0`; `IsDead` is zero health.
- `IsUnconscious` is `IsAlive && CurrentStun >= CurrentHealth`.
- `IsIncapacitated` is `IsDead || IsUnconscious || IsImmobilized`.
- Acting requires positive AP and no incapacity, plus session turn and availability gates (`CanUnitActNow`).

`AliveUnit` proves life and board presence, including unconscious bodies. Bodies stay in alive storage on their tile, block occupancy, and remain targetable; damage actions can affect them. Faction elimination and scheduling count living conscious units, independent of AP or temporary immobilization. Health-based survivor summaries keep unconscious participants alive.

### Damage, disabling, and recovery

`Damage` packets carry amount, element, and `DamageKind` (default `Health`). `DamageResolver` (`scripts/battle/combat/`) resolves them in bundle order inside `ApplyDamageTo`:

- Health packets consume remaining armor. An element match strips 1.5× armor (floored); health spill always uses the un-multiplied amount, so a match never increases spill.
- Stun packets bypass armor, leave it for later packets, and increase only `CurrentStun`.
- Non-positive packets resolve to zero.

`UnitDamagedBattleEvent` carries `ArmorDamage`, `HealthDamage`, and `StunDamage` plus the pre-mitigation bundle. Death emits `UnitKilledBattleEvent`; a new unconscious transition emits `UnitUnconsciousBattleEvent`; hits that cause neither emit `UnitDamagedBattleEvent`. A lethal bundle emits only the kill event, including death after earlier unconsciousness. Kill credit is death-only. Elimination objectives observe both event types; death playback and kill-only hooks keep consuming `UnitKilledBattleEvent`.

Status effects (`StatusEffectSpecData` in `scripts/items/effects/`) apply after survival. Each packet is gated by its own `ApplyChancePercent` and `RequiresHealthDamage`; a stun packet cannot borrow a sibling packet's health spill. `StatusEffectSystem` ticks at the owner's turn end: decrement, damage-over-time through the normal pipeline, expiry.

Recovery order at the ending faction's turn end: `StatusEffectSystem` and `ArmorRegenSystem` (priority −100), then `StunRecoverySystem` (priority −90). Only armor or health damage re-arms the armor regen delay. Stun recovery is a fixed `5u` per turn for living conscious units; unconscious and dead units recover nothing. A positive reduction raises `UnitStunRecoveredBattleEvent`; ended sessions recover nothing. Routing max-health debuff effects through the damage pipeline is deferred.

### Board objects versus units

`BattleObjectState` is an immovable placed object. Carrying `ObjectHealthCapability` makes it attackable; without it, it is inert scenery. `BattleEntity` wraps unit or object identity; `BattleRuntime.TryGetAttackTarget` mints its `AttackTarget` proof (`Some` only for a live unit or a live health-bearing object).

Feasibility (weapon, self, ally, liveness, attackability, visibility, range) resolves once through `AttackContext.Resolve` — the shared gate for `AttackEntity` and the `GetHitChanceForAttack` preview, so the two cannot drift. Against a unit, the attack rolls through the session's `IHitChanceCalculator` (default `StandardHitChanceCalculator`: Aim − cover, clamped 0–100) with the seeded RNG. Against an object the calculator is bypassed: a feasible object shot always connects. `weapon.TrySpendShot(attacker.ActiveBuffDamageMods)` spends ammo before the roll; a miss still consumes.

Object damage goes through `ApplyDamageTo`'s object overload: health packets reduce the capability, stun and status payloads are ignored, and zero health is the terminal `Destroyed` transition (occupancy clears before broadcast). Objects emit `ObjectDamagedBattleEvent` and `ObjectDestroyedBattleEvent`, never the unit damage events. Timed objects carry `TimedEffectCapability`; `SpecialObjectTimerSystem` expires them at turn end and resolves payloads before `ObjectExpiredBattleEvent`. `TimedObjectObjective` fails the mission when a tracked timed object expires or is destroyed.

Area effects resolve through `CapabilityEffectResolver`: a blast hits every unit and every live health-bearing object in the radius, friendly fire included, through the ordinary damage pipeline.

### Capture summary and campaign lifetime

The first `EndBattle` freezes capture membership before broadcasting `SessionEndedBattleEvent`. Victory with a designated player captures every living unconscious enemy, whatever caused the knockout. The list is read-only and deduplicates original `Combatant` references; the Combatants keep their identity and faction and never join the player's roster. `GetFactionEndOfBattleSummary` exposes `CapturedEnemies` only for that player's victory summary. Unconscious player participants remain survivors.

`GameState.Captivity` retains added Combatants by reference for the campaign's lifetime, deduplicates by reference, and returns detached snapshots. Standalone battle capture summaries work; transferring them into captivity and the campaign-to-battle handoff are deferred.

### IBattleSessionQuery

```csharp
public interface IBattleSessionQuery<out TResult>
{
  public TResult Execute(BattleSession session);
}
```

The result shape is contract:

- Bare `TResult` — the question always has an answer, usually because proof-typed inputs (`AliveUnit`, `ValidatedPoint`) carry the invariants. `CanUnitActNow` takes a raw `BattleUnitState` because `false` is a valid answer for any unit.
- `Option<T>` — absence is normal (`GetUnitAtTile`, `GetObjectivesForFaction`).
- `Either<BattleQueryFailure, TResult>` — genuinely fallible (`GetHitChanceForAttack` for an out-of-range or unseen target, `GetFactionEndOfBattleSummary` before the battle ends).

Scene code mints proofs at the runtime boundary: `TryGetAlive(unit)`, `TryGetAliveObject(obj)`, `TryGetAttackTarget(entity)`, and `TryGetTile(coordinates)`. A `None` there is the normal rejection path. Missing and dead units, and off-board tiles, are not query failures. Misusing the trusted core — a proof in a context it was never valid for, or a question the phase cannot answer — throws exceptions that are not meant to be caught. A `TryGetTile` proof cannot go stale because board bounds are fixed.

Examples: `FindPathForUnit`, `GetPossibleMoveTilesForUnit`, `GetVisibleEnemiesForUnit`, `GetHitChanceForAttack`, `GetAvailableActionsForUnit`, `GetPlayerFactionQuery`, `GetFactionEndOfBattleSummary`. One question per query; complex algorithms hide behind that specific query rather than turning `Query` into a dispatch switch.

### BattleBoardState

The board stores `BattleTileState[x,y,z]`, unit and object occupancy, and positions. Adjacency is eight-way: orthogonal plus same-level diagonals, and a diagonal step needs at least one open flank, so sealed corners cannot be cut. Pathfinding is a pure-C# breadth-first search with a fixed direction order, exposed as `FindPath`. Unit-specific questions stay in queries (`FindPathForUnit`, `GetPossibleMoveTilesForUnit`). See [battlescape-tile-system.md](battlescape-tile-system.md).

### Visibility

The session owns visibility because it derives from unit positions, tile state, and explored memory:

- `VisionStat` is the sight range of each living conscious observer.
- Visible tiles come from scanning a 3D bounding box within Euclidean range and raytracing line of sight per candidate. Whole-tile `BlocksLineOfSight` occludes; a diagonal between two blockers passes only when at least one flanking tile is open. Vertical passages through `BlocksVerticalLineOfSight` tiles are blocked.
- A unit is visible when it stands on a visible tile. Each unit stores its visible tiles and visible units; each faction stores explored-tile memory.
- A faction always knows its own living units. Unconscious observers contribute nothing; exploration persists, and conscious observers still see the bodies.
- After each committed event, only units whose cell or consciousness changed recompute visible tiles; every conscious observer rebuilds visible-unit membership wholesale.

Vision is omnidirectional; there is no smoke attenuation, lighting model, or last-known enemy memory.

### BattleAction and the executor

The built-in actions are `StartBattle`, `SpawnUnit`, `MoveUnit` (composite), `PlaceObject`, `InteractWithObject`, `AttackEntity`, `ReloadWeapon`, `ThrowItem`, `UseItem`, `ApplyDamage`, `PassUnit`, and `EndFactionTurn`.

```csharp
var runtime = new BattleRuntime(session);
var result = runtime.ExecuteAction(BattleAction.MoveUnit(unit, [destination]));
```

`Submit` (and the `ExecuteAction` facade) resolves the chain before returning one `BattleActionExecResult`: the submitted action plus every event committed while resolving it, as a zero-copy `ReadOnlySpan<BattleEvent>` into the executor's append-only log. A returned result does not imply the action finished: an interrupted action, or a battle that ends mid-submission, leaves the remainder unexecuted. Only a `Result.Rejected` step is converted — parameters are trusted, so `Submit` throws `InvalidOperationException`; arbitrary action or hook exceptions propagate unchanged after queued work is cleared. Committed state and events are never rolled back. Full rules: [battle-action-executor.md](battle-action-executor.md).

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

- The battle starts in `BattlePhase.Setup` and moves to `InProgress` through `StartBattle`, which builds the round queue from factions with conscious forces.
- `EndFactionTurn` is the explicit faction-turn action. `PassUnit` ends a unit activation and advances the turn when no actable unit remains on the side.
- Turn start is uniform at both invocation sites: refresh the faction's action-point availability, raise the turn-start events (`TurnStarted`, plus `ActiveSideChanged` on a side flip or `SessionStarted` on battle start), then refresh per-unit AP from the possibly buffed `MaxActionPoints`. Mid-dispatch observers of `SessionStarted`/`TurnStarted` see pre-refresh AP; this is deliberate, because the refresh completes before `StartBattle`/`Submit` returns.
- Unit death updates alive/dead storage, occupancy, availability, and queue membership. Unconsciousness consumes availability and drops the faction from future turns when no conscious ally remains, while the body stays on its tile. A disabled active faction keeps the queue head until explicit turn end.
- The player's loss of all conscious forces ends the battle as defeat. `ObjectiveSystem` suppresses directives on that final event so an objective cannot override the wipe backstop. Without a designated player, total knockout stays in progress until turn end resolves a draw.

## Battle Events

Events are records rooted at `BattleEventTag`; hook keys are event types or shared marker interfaces (`IUnitBattleEvent`, `IPositionedBattleEvent`, `ICausedByUnit`). Presentation observes `BattleSession.BattleEventCommitted` (or the runtime re-raise), which fires only after the state change committed.

- Session and turns: `SessionStarted`, `SessionEnded`, `TurnStarted`, `TurnEnded`, `ActiveSideChanged`.
- Units: `UnitAdded`, `UnitActivationEnded`, `UnitMoved`, `TileOccupied`, `UnitAttacked`, `UnitReloadedWeapon`, `UnitDamaged`, `UnitKilled`, `UnitUnconscious`, `UnitStunRecovered`, `UnitArmorRegenerated`, `UnitSpotted`.
- Statuses and buffs: `UnitStatusEffectApplied`, `UnitStatusEffectTicked`, `UnitStatusEffectExpired`, `UnitBuffActivated`, `UnitBuffDeactivated`.
- Items: `ItemThrown`, `ItemUsed`, `CapabilityResolved`.
- Objects: `ObjectPlaced`, `ObjectInteracted`, `ObjectExpired`, `ObjectDamaged`, `ObjectDestroyed`.
- Objectives: `ObjectiveAdded`, `ObjectiveCompleted`, `ObjectiveFailed`.

Hooks register on the executor and fire once per committed event, after the broadcast. Default systems: status effects and armor regen (`TurnEndedBattleEvent`, priority −100), stun recovery (−90), capability effects (`ItemThrownBattleEvent`), buff evaluation (`TurnStartedBattleEvent` and `UnitAddedBattleEvent`), and `ObjectiveSystem` (catch-all `BattleEventTag` router at +100, filtering each objective's declared keys). No enum, no phase split: single post-broadcast firing.

## Deferred Extensions

`BattleRules`, a battle AI controller, movement- and targeting-query strategies, and reaction fire are still future work. When they arrive, they must read through typed queries, commit through actions, and emit events — never mutate scene state.

## Canonical Vocabulary

`BattleSession`, `BattleAction`, `BattleActionExecResult`, `BattleActionExecutor`, `BattleHook` (`BattleHook<TEvent>`, `HookContext`), `IBattleSessionQuery<TResult>`, `BattleQueryFailure`, `BattleRuntime`, `Either<BattleQueryFailure, TResult>`, `BattleEventTag`, `BattleBoardState`, `BattleTileState`, `BattleUnitState`, `BattleEvent`, `BattleScene`, `BattleUiController`, `EventPlaybackDirector`.
