# Codex architecture reference

This document condenses the core contracts. Read the linked detail document before changing the corresponding subsystem.

## Architecture

### Proof-typed inputs
Use argument types that prove the required invariants. When a proof type is impractical, throw an exception for the caller bug. Let the exception propagate.

### Data / Runtime split
Each game concept has two layers. Detail: [equippable-item-architecture.md](equippable-item-architecture.md), [stat-system.md](stat-system.md).
- **`*Data`** — The `*Data` layer holds authored serializable `Godot.Resource` templates (`.tres` under `resources/`) as immutable definitions. The hierarchy is `NamedEntityData` → `EquippableItemData` (→ `WeaponData` → `AmmunitionedWeaponData` → `FirearmWeaponData`). It also includes `CombatantData`, `FactionData`, and `Ammunition`. Non-weapon items use one `EquippableItemData` plus capability resources in its `Capabilities` array. The array holds `ThrowableCapabilityData`, `BlastCapabilityData`, `ChargesCapabilityData`, `ModSlotsCapabilityData`, and `ArmorCapabilityData` in `scripts/items/capabilities/`. The armor capability carries `ArmorStat : BaseArmorStat` plus regen config.
- **Runtime wrapper** — The runtime layer uses plain mutable C# objects and mirrors the data hierarchy. `EquippableItem` and its `Weapon` subclasses hold runtime capabilities created via `CreateRuntime`. Lookup goes through `item.FindCapability<TCap>() : Option<TCap>`. Capability proofs use `item.With<TCap>() : Option<ItemWith<TCap>>`. `ThrowItem` requires `ItemWith<ThrowableCapability>`. The layer also defines `Combatant` and `Faction`.
  - `SpawnUnit`/`AddUnit` carry `BattleUnitState.EquippedArmor : Option<ItemWith<ArmorCapability>>`.
  - Deployment-time stat modifiers arrive only through the explicit `UnitLoadout` (`BattleSetup`) input. `GatherStatContributions` applies them exactly once at spawn, between combatant contributions and weapon/buff contributions. `StatMod` resources are shared and immutable by convention.
  - Inventory and equipment code depends on runtime base types.

### Stat system
A stat's identity is its concrete subclass. `scripts/stats/concrete_stats/` holds the concrete subclasses of the abstract `Stat` base. Typed fields accept only that stat. Runtime lookup goes through `HasStats.GetStat<TStat>()`/`TryGetStat<TStat>()`. Modifiers form a hierarchy: `StatModifier` (numeric op) → `StatMod` (`TypedStatMod<TStat>`) → `EquippableMod` (`MultiStatMod` for many).

Add a concrete subclass for each new stat or modifier. Items hold armor through `BaseArmorStat` on `ArmorCapabilityData`. Detail: [stat-system.md](stat-system.md).

### Battle runtime (`scripts/battle/`)
The session/board/queries/actions/executor/hooks core is scene-independent C# and the single source of tactical state. Presentation observes the runtime. (`map/`, `types/`, `objects/` hold authored Godot resources.) Detail: [battlescape-architecture.md](battlescape-architecture.md), [battle-session-command-pattern.md](battle-session-command-pattern.md), [battle-action-executor.md](battle-action-executor.md), [battlescape-tile-system.md](battlescape-tile-system.md), [battle-map-authoring.md](battle-map-authoring.md).

- **Battle startup** — `BattleFactory` owns startup and exposes it only through `Start(BattleTypeData, int?, Option<PlayerDeployment>)` and `Start(BattleSetup, IHitChanceCalculator?)`. The first overload composes `BattleSetupResolver.Resolve` with `Start(BattleSetup, IHitChanceCalculator?)`.
  - `BattleSetupResolver.Resolve` chooses the map from the pool with the seed and builds ordered sides into one grouped `BattleSetup`. `MapDeployment.AssignSpawns` pairs loadouts with X/Y/Z-sorted spawn cells. A shortage yields `SpawnSlotShortfall`.
  - `Start(BattleSetup, IHitChanceCalculator?)` builds the board and parses the complete layout into typed `BattleSetupFailure`s before any side effect.
  - The factory then materializes the complete roster and objects and instantiates the objectives. It constructs the session with a scheduler valid from the real roster. Then it constructs the runtime and the default hooks. A roster without an eligible first faction is a construction failure.
  - Declared systems register through the narrow `BattleHookRegistration` capability (no runtime handle, queries, or submission).
  - One private factory-owned bootstrap then runs through the ordinary event dispatch. Every placement announcement precedes the initial spotting announcements. Hooks see the complete roster and availability. Session start precedes the first turn and its action point (AP) refresh while the battle is ongoing. Interrupts run only after the startup primitive. The factory owns all setup.
  - A supplied `PlayerDeployment` preserves its campaign faction, combatants, and `Combatant.OwningFaction`. Mismatches are typed failures.
  - A fault before the runtime exists stops startup before any object needs disposal. A fault after construction disposes the runtime and rethrows the original exception.
  - A start whose terminal objective ends the battle during startup succeeds and returns the completed runtime.
  - Map authoring detail: [battle-map-authoring.md](battle-map-authoring.md).
- **Entity and map authoring** — `BattleEntityAnnotator` marks one `MeshInstance3D` model with a brush MeshLibrary on its native Annotations GridMap. Mesh metadata (`footprint_data`) stores the brush defaults. A coordinate override fully replaces the brush flags. Export bakes the resolved footprint into `BattleProp` mesh metadata (`battle_footprint`). The tile palette is a native MeshLibrary.
  - `BattleMapAuthoring` expands whole footprints from its Floors/Props anchors onto one walkable Y=0 layer. The bake rejects conflicting volumes, unsupported upper floors, and invalid spawns.
  - Bake assigns `MapData` to the authoring root. Saving retains it in the scene. Export saves a separate `BattleMap` scene with owned GridMaps. The runtime consumes that scene directly.
  - Procedures: [battle-map-authoring.md](battle-map-authoring.md).
- **`BattleSession`** — `BattleSession` is the authoritative state holder. It holds completion (`Option<BattleOutcome>`), turn number, active side, faction order, unit pool, alive/dead storage, per-faction visibility/explored memory, and frozen capture membership. The factory builds it from a complete materialized layout.
  - Its `TurnScheduler` establishes the initial queue, active side, and availability at construction. Each subsequent `Advance()` result establishes the next availability and reports round/exhaustion. `Stop()` retains the last active identity.
  - The session owns `TurnNumber`, the AP refresh, and event announcement. An eliminated active side keeps its queue head until explicit turn end.
- **Life and consciousness** — `IsAlive` is `CurrentHealth > 0`. `IsUnconscious` is `IsAlive && CurrentStun >= CurrentHealth`. `IsIncapacitated` is `IsDead || IsUnconscious || IsImmobilized`. Acting requires AP, a capable actor, and the session's turn and availability checks.
  - Unconscious bodies keep their tile, block movement, stay in alive storage, and remain targetable. Their observer caches are empty.
  - Faction vision comes from conscious units. Elimination and scheduling count living conscious units, regardless of AP or immobilization.
- **`BattleBoardState`** — The board stores a `BattleTileState[x,y,z]` grid, occupancy, and eight-way adjacency. A diagonal step needs one open flank. A pure-C# breadth-first search with fixed direction order provides pathfinding. Coordinates are the engine-agnostic `Vector3I` (X width, Y height, Z depth).
- **Reads** — Gameplay code reads state through `IBattleSessionQuery<TResult>` questions via `runtime.Query(...)`. The result shape is contract:
  - Reads with proof-typed inputs return bare results.
  - Reads with normal absence return `Option<T>`.
  - Fallible reads return `Either<BattleQueryFailure, TResult>`.
  - Reads with a guaranteed answer keep that guarantee after completion. `GetBattleOutcomeQuery` is `None` while the battle is ongoing. `GetActiveSideQuery` resolves because the scheduler retains the last active side. `CanUnitActNow` is total for any unit and returns `false` after completion.
  - Scene code creates proofs via `BattleRuntime.TryGetAlive`/`TryGetAliveObject`/`TryGetAttackTarget`/`TryGetTile`. A `TryGetTile` proof remains valid for the board's lifetime.
  - Extend reads through queries. Detail: [battle-session-command-pattern.md](battle-session-command-pattern.md).
- **Writes** — All gameplay mutation goes through `executor.Submit(action)`. `Submit` returns one `BattleActionExecResult`. The result holds the action plus its committed events, as a zero-copy span of the append-only log. Construct and submit each action immediately against the same battle state.
  - Parameters are trusted. `Submit` converts `Result.Rejected` to `InvalidOperationException`; a rejection is a caller bug.
  - A failed submission clears queued work and preserves committed state and events. Action or hook exceptions propagate unchanged after the cleanup.
  - A submission to a battle whose outcome is already decided is also a caller error. `BattleRuntime.ExecuteAction` and executor `Submit` share one admission guard. The guard rejects the submission before the `ActionStarted` signal, before any queue mutation, and before any committed event.
  - Completion during a submission is a normal case. The current step's atomic effect and causal event dispatch finish. The executor drops the remaining steps and queued interrupts. The submission returns its committed events with a normal result.
  - Actor actions re-check mutable liveness, incapacity, possession, equipment, and charges at `Execute`. A changed fact yields `Result.Interrupted`. Composite actions (e.g. `MoveUnit`) commit one atomic step per `Execute`. Hook interrupts enter the front of the queue after a step commits. Reactions act on post-step state.
  - Detail and hook ordering: [battle-action-executor.md](battle-action-executor.md).
- **Combat** — `AttackEntity` against a unit rolls via the injected `IHitChanceCalculator` (default `StandardHitChanceCalculator`: Aim − cover, clamped 0–100) plus the seeded RNG. Against a destructible object the attack bypasses the calculator. A feasible object shot always connects. Target feasibility resolves once via `AttackContext.Resolve`. The action and the `GetHitChanceForAttack` preview share this check, so both callers use the same feasibility rules.
  - `weapon.TrySpendShot(attacker.ActiveBuffDamageMods)` spends ammo before the roll. A miss still consumes. The spend yields `Damage` packets (amount + element + `DamageKind`, default `Health`).
  - The pure `DamageResolver` splits health packets into armor damage and health damage. Armor damage is 1.5× floored on element match. Health damage stays un-multiplied. Stun packets bypass armor and health.
  - Only armor damage or health damage restarts regen. `ArmorRegenSystem` ticks at the owning faction's turn end.
  - Damage packets may carry status specs, applied after survival and gated per packet by `ApplyChancePercent`/`RequiresHealthDamage`. `StatusEffectSystem` ticks at the owner's turn end. `IsImmobilized` blocks acting.
- **Board objects** — `BattleObjectState` is an immovable placed object. `ObjectHealthCapability` supplies attackability. `BattleRuntime.TryGetAttackTarget` creates the `AttackTarget` proof for either kind.
  - Objects take the object damage overload. The overload ignores stun and status payloads. Zero health causes the `Destroyed` transition. The board clears occupancy before the broadcast. Objects emit `ObjectDamagedBattleEvent`/`ObjectDestroyedBattleEvent`.
  - Blasts hit every unit and live health-bearing object in radius. Friendly fire is included.
  - Timed objects expire at turn end via `SpecialObjectTimerSystem`. The system resolves payloads before `ObjectExpiredBattleEvent`. `TimedObjectObjective` fails the mission when a tracked object expires or is destroyed.
- **Death and recovery** — Death emits `UnitKilledBattleEvent`. A new unconscious transition emits `UnitUnconsciousBattleEvent`. Other hits emit `UnitDamagedBattleEvent`. A lethal bundle emits only the kill event, including death after earlier unconsciousness. Kill credit is death-only.
  - `StunRecoverySystem` recovers a fixed `5u` at the owner's turn end, after status effects and armor regen. Recovery requires a living conscious unit.
  - Turn-end upkeep order: `StatusEffectSystem` and `ArmorRegenSystem` (−100), then `StunRecoverySystem` (−90).
- **Buffs** — The buff system uses shared `Buff`/`BuffCondition` resources in `scripts/buffs/`. `BattleUnitState` stores an ordered `(Buff, IsActive)` entry per grant and owns evaluation.
  - Grants at spawn come from `Combatant.InnateBuffs` plus `BuffGrantCapabilityData` on equipped weapon/armor. Identical grants stack. Hooks re-evaluate post-broadcast (`TurnStartBuffHook`, `UnitSpawnedBuffHook`). Turn-start flips apply before the AP refresh.
  - Each flip caps current health at the new maximum, with a minimum cap of 1. It raises the activation event before the next grant.
  - Active snapshots feed `StatMod`s through `GatherStatContributions` and `DamageBundleMod`s through `TrySpendShot` after slot and ammunition mods. The unit owns activation state for the shared resources.
- **`BattleRuntime`** — `BattleRuntime` is a disposable facade over session + executor. It exposes `Query(...)`, the proof methods, `RegisterHook`/`UnregisterHook`, and `ExecuteAction`. `ExecuteAction` raises `ActionStarted`/`ActionCompleted`. The runtime re-raises `BattleEventCommitted`. The re-raise subscription happens before the factory constructs the executor, so subscribers observe cause events before follow-up events from hooks.
- **Hooks** — One mechanism covers all hooks: `BattleHook` (+ `BattleHook<TEvent>`), keyed to event-tag types, with an int priority (lower first, then registration order). The registry belongs to `BattleActionExecutor` and fires once per committed event, post-broadcast.
  - Hooks may mutate the session and queue follow-up events. The committed stream stays linear. Hooks return interrupt actions from `OnEvent` for the executor to submit after the step. Interrupts require an in-flight action. This rule relies on caller discipline.
  - `BattleRuntime` owns the session's single executor. Tests reuse it through `BattleFixture.Runtime`.
  - Construction establishes availability for the first turn. `Advance()` establishes it for later turns. Both dispatch turn-start events before AP refresh.
  - The first turn's refresh covers all living units. Later turns cover only the turning side's living units.
  - Buff flips apply before the refresh reads (possibly buffed) `MaxActionPoints`. A dispatch that decides the battle skips the refresh. All upkeep is event-keyed.

### Presentation boundary
Scenes in `scenes/battle/` include the `BattleScene` host, the standalone `BattleLauncher` host, `BattleUiController`, `BattleInputController`, `EventPlaybackDirector`, and the authored `BattleUI` view. `BattleScene` receives a started runtime via `Present` and instances `setup.MapScene`. `BattleUiController` owns the selection/targeting FSM with memoized read-only `UnitActionOption` adapters. The adapters delegate live availability and create fresh `AliveUnit` proofs for actions. `BattleInputController` picks tiles. `EventPlaybackDirector` subscribes `ActionCompleted` and plays committed events in commit order.

The UI owns selection. Scene code reads through `runtime.Query(...)` and submits actions through the runtime.

Character models (`scenes/models/`) are presentation-owned. One `CharacterModel` root resolves its native `AnimationTree` and owns mesh masks, clothing, and attachments. Import-validated catalogs feed instance-scoped controls. `Selections` is the sole serialized clothing state. API and authoring guide: [model-controls.md](model-controls.md).

### Capture and campaign lifetime
The first `EndBattle` freezes a read-only, reference-deduplicated list of every living unconscious enemy before `SessionEndedBattleEvent`. Capture happens on victory with a designated player only. `GetFactionEndOfBattleSummary` exposes `CapturedEnemies` only for that player. Capture retains original Combatant references and preserves roster membership. Unconscious player participants remain survivors.

`GameState.Captivity` retains added Combatants by reference for the campaign's lifetime. Repeated `Add` of a held reference is inert. `Combatants` returns a fresh unordered snapshot.

Standalone summaries work. Captivity transfer and the campaign-to-battle handoff are deferred. Construction stamps authored `CampaignStartData.StartingCaptives` into `Captivity` as fresh Combatants that keep their own faction. Roster membership remains unchanged.

### Combatant conditions
- `GameState.Conditions` — One `CombatantConditionSystem` per campaign (`scripts/strategic/`) owns persistent injury and fatigue as separate records. Each record holds a positive tier position plus an absolute-tick recovery deadline. Records exist per combatant only while active. A combatant without an entry is Healthy/Fresh. Full recovery removes the key.
  - Authored `CombatantConditionRulesData` resources define the ladders with arbitrary tier counts.
  - `ValidateAndSort()` runs at campaign construction from `CampaignStartData.ConditionRules` and rejects malformed tiers before recovery begins. The defaults are injury 4/6/16/30 days and fatigue 1/4/8 days; one day = 1440 ticks.
- Mission returns update the records. `GeoscapeSession.ApplyMissionReturn` converts reported mission health damage (percent of battle-effective max health) into injury tiers. The tiers add to the current position and cap at the ladder's final tier. The deadline restarts from the return tick. Zero-damage returns preserve the injury record.
- Fatigue accrues one position per return, capped, only for soldiers uninjured before that return. Both records can coexist. Both modifier sets apply. `HealthByCombatant` covers living participants only (unconscious included). All survivor records apply before the first `CombatantConditionsChanged` broadcast. Experience awards use a separate progression path.
- Tier penalties are multipliers on health/aim/movement (injury) and aim/will (fatigue). Penalties include an unconditional one-hit-point health floor while any injury is active. `CombatantConditionSystem.StatContributions(combatant)` contributes the penalty set. `GameState.CampaignStatContributions(combatant)` is the single campaign composition policy (own mods, faction, progression/rank, conditions, equipped weapon) feeding soldier screens. Battles receive condition penalties only as explicit deployment-time `UnitLoadout.StatMods`.
- Each advancing tick ends with roster recovery, after time, scheduled events, expiry, manufacturing completion, and research completion. Ladders improve one tier per elapsed chained deadline. Fully recovered conditions lose their key. The session broadcasts one `CombatantConditionsChanged` per changed identity. Recovery persists across session rebuilds; paused time and pending resolutions freeze it. Only the campaign roster recovers.
- `GeoscapeSession.CanDeploy(combatant, mission)` returns `false` for an injured combatant or a combatant at the top fatigue tier. Despite the restriction, the mission's `AllowUnfitDeployment` authorizes an exceptional deployment. Penalties still apply. `SquadLoadoutView` consumes it. The future battle-launch handoff must supply penalties explicitly through `UnitLoadout.StatMods`.

### Strategic layer and battle authoring
The strategic runtime is in `scripts/strategic/` (`GeoscapeSession`, scheduled events, `RegionData`). `scenes/geoscape/` presents it (`GeoscapeScene`, map control, HUD, event adapter). Battle authoring types and objects are in `scripts/battle/map/`, `scripts/battle/types/`, and `scripts/battle/objects/` (authored Godot resources). Authored `.tres` files are under `resources/battle_types`/`resources/battle_objects`.

### Geoscape view stack
- The map HUD keeps Units, Engineering, Captivity, and the manufacturing summary in one bottom row. Date, time, pause, and speed share the top-left group. Events open through map markers. Scheduling, expiry, pending-resolution gating, and marker identity stay session-driven.
- The root window uses native `canvas_items` scaling with `expand`, an 800×600 logical minimum, and a 1600×900 startup override. The geoscape camera frames the authored map once after initial HUD layout and stays fixed.
- `GeoscapeViewManager` owns the stack of `GeoscapeView : Control` nodes. Navigation is signal-driven over that stack. The map and HUD form the permanent root view in an interactive SubViewport, with the full-rect HUD as a sibling above it. Views export an optional `BackButton`, request navigation through signals, and keep state while covered. `ViewChanged` triggers the single binding entry point, `Present(GameState, GeoscapeSession)`.
- `Push` accepts only fresh, unparented, live views. Caller bugs throw before the code touches the active view. `Push` preserves authored anchors and hides, processing-disables, and disconnects covered views. `Pop` frees the top immediately. Popping the root preserves the stack. Subscriptions exist only for the stack top.
- The pending-resolution dialog is a stacked view: `GeoscapeEventResolution` renders `session.PendingResolution` and emits `Resolved` to `CompleteResolution`. The composition root pushes it on `ResolutionEventOpened`. It pops the view only on the session's committed close event. Resolution completion controls the close path.
- Event dialogue (`scenes/dialogue/DialogueView.tscn`) opens over the resolution view when the event carries a `DialogueSequenceData`. While top, clicks and `ui_accept` advance the typewriter. The final advance emits `Finished` and pops via `RequestBack`. Campaign time stays frozen while covered. A standalone host consumes `Finished` and frees the view.
- Mission preparation (`scenes/geoscape/squad/`): The dialog's Engage produces a fresh `SquadLoadoutView` (mission pending, time frozen). Call `Configure(heading, capacity, allowEquipmentEditing, mission)` before `Present`. Mission preparation configures three editable slots. Interrogation configures two slots with editing disabled and mission context unset. Re-selecting a squad member is inert. `GetSelectedCombatants()` returns a fresh list in slot order.
- Squad view details: Under a mission, `CanDeploy(combatant, mission)` controls roster choice availability. An unset mission context permits unrestricted selection. Condition labels and stat summaries render from the campaign registry and `CampaignStatContributions`. Equipment editing happens in the nested `UnitView`, applies immediately to campaign state, and Back discards only the selection. Battle launch is a deferred handoff; the adapter builds each `UnitLoadout` explicitly from the selected combatant's current `EquippedWeapon`/`EquippedArmor` values. It carries the campaign's current condition modifiers as the loadout's stat mods.
- Captivity inspection (`scenes/geoscape/captivity/CaptivityView.tscn`) shows a read-only view of `GameState.Captivity.Combatants` (faction, stats, equipment, innate buffs). Interrogation preparation opens the shared squad view with two slots and editing disabled. The captive selection survives Back. Interrogation launch is a deferred typed payload carrying the selected captive identities plus the roster interrogators as explicit data. Capture transfer also remains deferred.

### Engineering
- Engineering registers only `CampaignStartData.ManufacturableItems` at campaign creation, preserving order and deduplicating references. Research completion registers its captured item references idempotently. Each recipe captures duration and stock policy at first registration. Registration grants manufacturing permission. Manufacturing completion adds stock.
- The queries belong to `EngineeringState`. `GetManufacturingOptions(armory)` returns a snapshot list of current options. `ResolveManufacturing(item, armory)` returns an eligible registered project or a typed failure. `GeoscapeSession` delegates. Start through `StartManufacturing` (`Either` failures), one job at a time. The conversion checks day-to-tick bounds and rejects deadline overflow.
- Completion clears the job and returns `ManufacturingCompleted`. The session applies Armory output before broadcasting. Completion notification requires a successful stock effect. A failed effect propagates and leaves the job cleared. Session rebuilds preserve campaign progress.
- Armory starts with shelves only for initial stock. First completion creates the shelf with the project's stock policy. An unlimited shelf permits withdrawal and excludes further manufacturing. Exhausted scarce shelves remain at zero. Each withdrawal creates a fresh runtime item.
- The authored screen `scenes/geoscape/engineering/EngineeringView.tscn` binds on activation, runs `StartManufacturing`, and shows failures. The compact HUD shows the active item and rounded-up remaining days. Updates follow committed events.
- The tick order runs time, scheduled events, expiry, manufacturing completion, research completion, then roster recovery.

### Research
- `GameState.Research` owns the project catalog, completed projects, and one optional active job. Authored `ResearchProject` resources define duration, a condition, and manufacturing unlocks. Conditions are shared read-only resources. The evaluation runs against supplied campaign state.
- The system computes availability on demand; available snapshots exclude the active project. Conditions may relock incomplete projects, and the system rechecks them at start. Launched jobs continue to completion. Starting requires an incomplete project. Built-in Always/completion/All/Any/Not condition resources compose directly; construction rejects malformed graphs.
- `GeoscapeSession` schedules with checked deadline conversion. `ResearchState` records completion and returns captured item references. The session registers those references with Engineering before broadcasting `ResearchCompleted`. Manufacturing supplies Armory stock. Rebuilding preserves progress.
- Zero-day research completes at the next research-completion phase during clock advancement. `StartResearch` schedules the job.
- Completion-effect exceptions propagate before the success notification, with committed state retained. Recovery after failure belongs to the caller. Paused time and pending resolutions freeze both jobs.
