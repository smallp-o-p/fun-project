# Battlescape Tactical Runtime Architecture

This document is the target architecture reference for the tactical combat layer. It describes the runtime system boundaries we want to build toward for an X-COM-style battle in Godot, based on the useful separations in OpenXcom without copying its SDL-era implementation shape.

## Design Goals

- `BattleSession` is the single source of truth for live tactical state.
- Resource templates such as `CombatantData`, `WeaponData`, and faction resources feed runtime state but never hold mutable battle progress.
- Tactical systems operate on session state through explicit responsibilities instead of blending rules, rendering, and input together.
- Godot scene nodes present the battle and collect player input, but they do not own combat truth.
- Action resolution is step-based so movement, visibility refresh, and interrupts such as reaction fire can occur mid-action.

## Component Diagram

```mermaid
flowchart LR
    subgraph Templates["Static Definitions (Godot Resources)"]
        CombatantData["CombatantData"]
        WeaponData["WeaponData / FirearmWeaponData / AmmunitionData"]
        WeaponModData["WeaponModDefinition\nstat modifiers + battle effects"]
        BattleEffectData["BattleEffectDefinition\nburn, pierce, chain,\nreaction modifiers, reload rules"]
        FactionData["FactionData"]
        BattleConfig["Mission / map setup data"]
    end

    subgraph Runtime["Authoritative Tactical Runtime"]
        BattleSession["BattleSession\nsingle source of truth"]

        subgraph RuntimeState["Mutable Session State"]
            BoardState["BattleBoardState\nBattleTileState[x,y,z]\ncover, occupancy, hazards, objectives"]
            UnitState["BattleUnitState[]\nposition, AP, facing, stance,\nvisibility, status effects, inventory refs"]
            ItemState["BattleItemState[]\nammo, ownership, tile drops,\nweapon runtime state"]
            EventStream["BattleEvent stream"]
        end

        subgraph Systems["Battle Systems"]
            TurnSystem["BattleTurnSystem\nturn order, side changes,\nselection, refresh, end turn"]
            ActionExecutor["BattleActionExecutor\nintent queue, step resolution,\ninterrupt handling, animation hooks"]
            Visibility["BattleVisibilitySystem\nLOS, FOV, detection,\nreaction-fire candidates"]
            Pathfinder["BattlePathfinder\nroute generation, reachable tiles,\npath preview, movement cost"]
            Rules["BattleRules\nhit, damage, AP cost,\ncover, deterministic combat math"]
            Effects["BattleEffectSystem\nweapon mod hooks,\nstatus application,\nsecondary effect generation"]
            AI["BattleAIController\ndecision making for non-player units"]
            Generator["BattleGenerator\nspawn units, seed board,\ninstantiate runtime state"]
        end
    end

    subgraph Presentation["Godot Presentation / Input"]
        SceneController["BattleSceneController\ninput translation, selection UX,\ncamera orchestration"]
        BattleScene["BattleScene / scene nodes\nmap visuals, units, VFX, HUD"]
        HUD["Battle HUD\nunit panel, action bar,\nturn controls, previews"]
    end

    Templates --> Generator
    Generator --> BattleSession
    WeaponModData --> Generator
    BattleEffectData --> Effects

    BattleSession --- BoardState
    BattleSession --- UnitState
    BattleSession --- ItemState
    BattleSession --- EventStream

    SceneController -->|"BattleActionIntent"| ActionExecutor
    HUD -->|"BattleActionIntent"| SceneController
    BattleScene -->|"selection / hover / click"| SceneController

    SceneController --> Pathfinder
    Pathfinder --> BattleSession
    Pathfinder --> Rules

    TurnSystem --> BattleSession
    ActionExecutor --> BattleSession
    ActionExecutor --> TurnSystem
    ActionExecutor --> Rules
    ActionExecutor --> Effects
    ActionExecutor --> Visibility
    ActionExecutor --> Pathfinder
    ActionExecutor -->|"emit"| EventStream

    Visibility --> BattleSession
    Visibility --> Rules
    Visibility -->|"emit spotted / hidden / reaction-ready"| EventStream

    Effects --> BattleSession
    Effects --> Rules
    Effects -->|"structured follow-up effects"| ActionExecutor
    Effects -->|"emit status / hazard / proc"| EventStream

    AI --> BattleSession
    AI --> Rules
    AI --> Pathfinder
    AI -->|"BattleActionIntent"| ActionExecutor

    BattleSession -->|"query only"| SceneController
    EventStream --> BattleScene
    EventStream --> HUD
    BattleScene -->|"redraw / animate only"| EventStream
```

## Ownership Rules

- `BattleSession` owns all live tactical truth.
- `BattleTurnSystem`, `BattleActionExecutor`, `BattleVisibilitySystem`, `BattleEffectSystem`, and `BattleGenerator` are allowed to mutate `BattleSession`.
- `BattlePathfinder`, `BattleRules`, and `BattleAIController` may read widely, but only the executor, turn system, or explicit effect application path should commit battle-changing results back into session state.
- `BattleSceneController`, `BattleScene`, and HUD nodes translate input, display state, and react to events. They do not spend AP, move units, or mark visibility directly.
- Existing resource types map into the generator phase and runtime instantiation phase only.
- Weapon mods are split between passive stat modifiers and structured battle effects. Effects are resolved through the battle runtime, not by scene nodes or ad hoc script side effects.

## Representative Flow: Move With Visibility Refresh And Reaction Fire

```mermaid
sequenceDiagram
    autonumber
    actor Player
    participant HUD as Battle HUD
    participant Controller as BattleSceneController
    participant Path as BattlePathfinder
    participant Exec as BattleActionExecutor
    participant Session as BattleSession
    participant Vis as BattleVisibilitySystem
    participant Rules as BattleRules
    participant Event as BattleEvent stream
    participant View as BattleScene

    Player->>HUD: Select soldier and hover destination
    HUD->>Controller: Movement intent preview
    Controller->>Path: Request path + AP cost preview
    Path->>Session: Read board, unit, and occupancy state
    Path->>Rules: Query step costs and movement constraints
    Path-->>Controller: Path preview + legality + reachable result
    Controller-->>HUD: Show AP cost and path preview
    Controller-->>View: Highlight path tiles

    Player->>HUD: Confirm movement
    HUD->>Controller: Commit move intent
    Controller->>Exec: Enqueue MoveUnit intent
    Exec->>Session: Validate active unit and reserve action context

    loop For each step in path
        Exec->>Rules: Validate AP cost for next step
        Rules-->>Exec: Step legal / illegal
        Exec->>Session: Spend AP and move unit one tile
        Exec->>Event: Emit UnitMoved step event
        Exec->>Vis: Recalculate visibility from changed position
        Vis->>Session: Update spotted units / seen tiles / reaction candidates
        Vis->>Event: Emit visibility delta events
        Vis->>Rules: Score reaction-fire opportunities
        alt Enemy reactor has valid interrupt
            Vis-->>Exec: Trigger interrupt candidate
            Exec->>Session: Pause move sequence
            Exec->>Event: Emit ReactionFireStarted
            Exec->>Rules: Resolve shot and damage
            Rules-->>Exec: Hit / miss / damage result
            Exec->>Session: Apply damage, status, death, morale fallout
            Exec->>Event: Emit shot and damage events
            opt Moving unit can no longer continue
                Exec->>Session: End move intent early
                Exec->>Event: Emit MoveInterrupted
                Note over Exec,Session: Abort remaining path steps
            end
        end
    end

    Exec->>Session: Finalize action state
    Exec->>Event: Emit ActionResolved
    Event-->>View: Play movement, spotting, and shot visuals
    Event-->>HUD: Refresh AP, selection, visible enemies, and prompts

```

## Canonical Runtime Vocabulary

Use these names consistently in future implementation work, even before all files exist:

- `BattleSession`
- `BattleUnitState`
- `BattleItemState`
- `BattleActionIntent`
- `BattleActionExecutor`
- `BattleTurnSystem`
- `BattleVisibilitySystem`
- `BattlePathfinder`
- `BattleRules`
- `BattleEffectSystem`
- `BattleAIController`
- `BattleEvent`
- `BattleSceneController`

## Battle-Aware Weapon Mods

- Weapon mods should be able to contribute both numeric stat changes and structured battle behavior.
- Keep passive numeric bonuses in the existing stat-mod lane.
- Add a battle-effect lane for behaviors such as burning targets, piercing cover, modifying reaction-fire exposure, changing reload rules, or spawning secondary hazards.
- `BattleEffectSystem` should evaluate these effects at curated hook points such as:
  - before action validation
  - before hit resolution
  - before damage application
  - after damage application
  - on reload
  - on turn start / turn end
- Effects should return structured outcomes for the executor to apply, rather than mutating scene nodes directly.

## Mapping From Current Project Terms

- Your current `CombatantData`, weapon data resources, and `FactionData` remain template definitions.
- The current conceptual `Battle` layer should evolve into `BattleSession`, not exist beside it as a second authoritative runtime owner.
- Current runtime classes such as `Combatant` and `Weapon` are still close to template wrappers; future tactical work should split immutable definitions from mutable battle state explicitly.
