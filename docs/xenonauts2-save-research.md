# Xenonauts 2 — Save System & Game State Research

Research dive into the decompiled Xenonauts 2 source (`.xenonauts2-research/`, gitignored)
to inform our own save/persistence design. Three questions investigated:
(1) how game state is persisted, (2) how player unit rosters are stored,
(3) what state is persisted to deliver an XCOM-like campaign.

All file paths below are relative to `.xenonauts2-research/Xenonauts2/Assets/Code/`.
Key claims were spot-checked against the source.

## TL;DR

X2's save is a **full ECS world dump** in one file: a readable JSON metadata envelope
(name, geoscape date, playtime, difficulty, game version + commit hashes, compressed
screenshot) wrapping an opaque `worldData` blob (FullSerializer `fsData` → Odin binary →
zip → Base64). There are no per-subsystem save DTOs and no soldier DTOs — soldiers,
bases, research, UFOs are all entities in the Artitas ECS world, and saving snapshots the
whole thing. Authored definitions (templates) live outside the save as addressable
assets; the save stores instances + drift. Load deletes every entity and rebuilds the
world verbatim.

---

## 1. Persistence mechanics

### Pipeline

1. A `SaveGameRequest` world event (`Screens/Common/Events/8SaveGameEvents.cs`) is fired
   from quicksave, autosave, or the save menu. `SaveGameSystem.QueueSaveGameRequest`
   (`Screens/Common/Saving/SaveGameSystem.cs`) **enqueues** the request.
2. Screen-specific subclasses drain the queue **only at safe boundaries**:
   - Strategy: end of geoscape tick (`PostGeoscapeTickReport`), or when off the geoscape.
     Quicksave is blocked in modal UI states, game over, tutorial, and IronMan.
   - Ground combat: dedicated phase steps (`GCPhases.DeployPreSave/DeployPostSave/
     TurnPreSave/TurnPostSave`) under a game-lock tag; runs async on a thread pool.
     Quicksave requires `PlayerControl` UI state, no action in flight, and the local
     player owning the active turn. **Mid-battle saving is supported.**
3. `InternalProcessSaveGameRequest`: fire `PreSaveGameReport` (systems scrub state,
   e.g. the RNG is serialized into a `GlobalRNG` singleton entity) → send
   `SerializationRequestCommand` → `ArtitasSerializationSystem.HandleSerialization`
   copies the world into a `WorldState` and runs `TrySerialize<WorldState>` → attach
   metadata (date/playtime/difficulty segments, screenshot, next slot index) → hand the
   `SaveGame` asset to the content pipeline to write.
4. Content pipeline (`ContentManager/Tasks/SaveProcessTask.cs`): chained processors —
   `ObjectToVersionedAssetConvertor` → `VersionedAssetSerializer` (FullSerializer) →
   `JsonStringPrinter` (compressed JSON) → `FileHandleWriter` → `FileHandle.WriteString`
   (`Directory.CreateDirectory` + `FileStream(FileOptions.WriteThrough)` + UTF8).
5. IronMan: single-slot group plus a hidden backup copy. Autosave slot counts are capped
   per type (auto/quick) by filename-index rotation.

### Format & layout

- One JSON file per save. `worldData` and the screenshot are zip+Base64 strings inside
  the JSON (`Screens/Common/Saving/SaveGameConverter.cs`):
  `SerializeValue<fsData>(worldData, DataFormat.Binary)` → `ZipUtility.Zip` →
  `Convert.ToBase64String`. No encryption.
- Location: `My Documents/My Games/Xenonauts2/Saves/` (via `Constants.Folders.Save()`,
  `Libraries/Common/Code/Constants.cs`; refuses Dropbox/OneDrive paths).
- Naming: `Saves/<campaign>/<type>_<screen>-<index>.json` — the `SaveGameGroup`
  identity segments double as both directory structure and display metadata.

### ECS serialization details

- `WorldState` (`Libraries/Artitas/Artitas.Core/Code/Serialization/../Systems/WorldState.cs`)
  = alive entities + `ComponentMatrix` (one bag per component type, indexed by entity ID)
  + type tables + template-stripping bitsets.
- **Entity references serialize as bare integer IDs**; the `EntityConverter` recreates
  them on load (`IsAlive ? GetEntity : ForceCreateEntity`).
- **Template stripping**: a component identical to the entity's authored template is
  *omitted* from the save and re-instantiated from the template asset on load. Saves stay
  small and untouched data auto-inherits content fixes.
- **Load semantics**: `DeleteAllEntities()` → copy bags wholesale into the component
  manager → recompute composition bitmasks/families → template re-instantiation pass →
  scene-object links restored by `SerializedLinkSystem` → caches rebuilt.
- Versioning: `SaveGame.CanLoad()` gates on `version >= MIN_SAVE_VERSION`; missing
  component types deserialize as `null` (warning-only unless actually referenced);
  `TypeReplacement` remaps renamed types; legacy uncompressed `worldData` and legacy
  `$type` keys are supported.

### Load flow

`LoadGameCommand` → static `SaveGameSystem.LoadGame(FileDescriptor)`: *partial* load for
the menu (see below) → full `ContentManager.Load` → the target screen is entered with
`LoadGameParameters` → the screen's bootstrap queues `DeserializeSaveGameCommand` →
`ArtitasSerializationSystem.HandleDeSerialization` → optional RNG reseed
(`Constants.ReseedSaveOnLoad`, anti-scumming) → `PostLoadGameReport` (systems re-acquire
clock refs, rebuild caches, re-fire `PostDeSerializationTasksComponent` delayed tasks).

### Save menu trick

The save list uses **partial loading**: the JSON stream is truncated at a known field
marker (`,\"worldData\":`) and a synthetic closing postfix is appended, so only the
metadata prefix parses — listing saves never deserializes the world blob
(`Screens/Common/UI/Elements/AbstractSaveLoadMenuElement.cs`,
`ContentManager/Descriptors/SaveParameters/PartiallyLoadableAssetParameters`).

### GC saves

In-mission saves additionally store `map` (an `ArtitasScene` asset reference) and
`gcParameters` so the tactical scene can be rebuilt on load (variant maps re-run
generation with the stored params). The map itself is not world state.

---

## 2. Soldier roster storage

**There is no roster class and no soldier class.** A soldier is an entity conforming to
the `StrategyArchetypes.StrategyActor` archetype (`Screens/Strategy/Factories/
StrategyArchetypes.cs` ~892), composed from layered archetypes:

- `SharedCombatantDefinition` (`Screens/XenonautsArchetypes.cs` ~241): per-stat
  components — `HitPoints` + `UnmodifiedHitPoints`, `TimeUnits`, `Accuracy`, `Bravery`,
  `Defence`, `Reflexes`, `Strength`, `PsionicStrength`, `Morale`, `Stress`,
  `ResistancesComponent`, `MedalsComponent`, `InjuriesComponent` +
  `InjuriesDurationComponent` (per-injury recovery time). Each stat is a `RangeComponent`
  (min/value/max) so growth can raise the max.
- `SoldierBiography`: `NameComponent`, `PortraitLayersComponent`, `DateOfBirthComponent`,
  `NationalityComponent`, `FlagComponent`, `RegimentComponent`, `Rank`
  (data-driven enum), `CombatantMissionHistoryComponent`.
- `CanTrackExperience`: `ExperienceComponent` (per-mission XP, clamped),
  `ProgressComponent` (career points per attribute), `ProgressHistoryComponent`
  (escalating thresholds for diminishing gains), `HistoryComponent`.
- Equipment: the entity *is* an inventory — real item entities linked into its slots;
  `LoadoutProfile` support; `ActorVisualVariationsComponent` for armor colors etc.
- Roles share the archetype: soldier/scientist/engineer differ by `CombatRating` /
  `ScientificRating` / `TechnicalRating`; **`IsSoldier()` is literally
  `CombatRating > 0`** (`Screens/Strategy/Utils/StrategyActorEntityExtensions.cs:34`).
  Bitmask meta-flags (`NoExperienceGain`, `ExcludeFromSoldiersMemorial`, …) live in
  `StrategyCombatantMetaComponent`.

**The roster is a query, not a container**: every screen builds a `Family` (cached live
query) filtering `StrategyActor` + owned-by-player + linked-to-base-X + `IsSoldier`.
Recruitment: `HirePersonnelCommand` pulls a pre-generated recruit entity out of a
`HiringPool` (refreshed monthly, capacity = sum of Living Quarters buildings'
`PersonnelCapacity` vs. per-soldier capacity requirement).

**Lifecycle is an explicit state machine** (`ActorAssignment`): `Hireable → Available →
OnMission → Recovering / KilledInAction / MissingInAction / Fired`. Dead soldiers are
**not deleted** — KIA/MIA entities persist in the world (and thus in every save), which
is what feeds `SoldiersMemorialElement` for free.

**Strategy ↔ ground-combat transfer** (`Screens/GroundCombat/Parameters/
StrategyToGCMissionStartParameters.cs:184`): the strategy entity's components are cloned
onto a GC combatant template through a whitelist (`CombatantCopiedToGroundCombat`),
keeping the strategy Guid. Return trip: a `MissionCombatResult` object
(`Screens/Strategy/Data/MissionCombatResult.cs`) carries `RecoveredItems` +
`CombatantModifications` (diff-templates keyed by `GUIDReferenceComponent`) +
`CampaignModifications`; `GroundCombatMission` applies them through an ordered pipeline
(clear loadouts → apply combatant diffs → recover items → remove fatal wounds → level-ups
→ death phase → EVAC phase → restore loadouts → outcome effects). The core merge
instantiates each diff-template onto the live strategy entity matched by Guid
(`Screens/Strategy/Data/CombatMission.cs:43`).

**Progression**: `StrategyExperienceSystem.ApplyExperience` moves per-mission XP into
career progress, then `TryTransformProgressIntoAttributes` converts progress to stat
gains via `floor(progress / threshold)` with thresholds escalated by
`ProgressHistoryComponent`; rank is *derived* from total stat gains. Training programs
tick on geoscape time (`TrainingSystem.UpdateTraining`).

---

## 3. What campaign state is persisted

| Category | Representation | Persisted state |
|---|---|---|
| Clock | `GeoscapeClock` entity | `DateTime`, scheduler dict, per-`ClockStep` (Tock/Hourly/Daily/Monthly) last-fire stamps; entity creation timestamps on every entity |
| Difficulty | entity from authored `difficultyConfig` template | whole config instance |
| Money | `MainPlayerResources` entity | `Cash` + `OperationPoints`; `Income`/`Expense` entities; `FundingReportsComponent` **history** |
| Regions | `GeoRegion` entities | relationship status (Unknown→…→Lost), relations/infiltration XRange values |
| Research | per-base queue entities + `ProjectTask` entities | FSM state (Active/…), `ProgressPoints`, `UnlockedStateMachine` gating; scientists in lab slots |
| Manufacturing | mirrored system + `Inventory` entities | workshop queues, batch `Quantity`, item stockpiles, aircraft `UnderConstruction` |
| Bases | `GeoBase` + `Building` entities | grid layout, construction FSMs + progress, personnel slots, power |
| Personnel | `StrategyActor` + `HiringPool` entities | roster (see §2), hiring pool contents, in-transit hires |
| UFOs / missions | `AlienAircraft`, `Mission` entities | FSM states (UnderConstruction/Airborne/Crashed/…), behaviour FSMs, activity progress, `DetectionState` per detectable |
| Scheduling | scheduler + `TimelineComponent` | pending `IScheduledTask`s, which timeline effects fired (counters) — **persisted mid-flight** |
| Interceptors | `XenonautAircraft` entities | fuel, repair/re-arm state, loadouts, patrol state |
| Victory | objective entities | `DoomsdayCounter`, funding/region/time objectives, endgame mission tags |
| Meta | globals | RNG seed (`GlobalRNG` singleton), `CampaignPlayTime`, `CreatedVersion` |
| Mid-battle | GC save extras | `map` asset ref + `gcParameters` + full world snapshot |

**Autosave policy**: every N in-game days (configurable); synchronously *before* entering
ground or air combat; after combat resolution; on game over. Skipped during tutorial.
IronMan = one slot + hidden backups.

**Not persisted** (re-derived): system caches (timeline cache, families, pause counter,
time speed — reloads paused), UI/notification state, authored definitions (templates,
tech tree, item stats, map scenes), and the tactical map geometry itself.

---

## 4. Takeaways for our implementation

Mapping X2's patterns onto our `*Data` (authored `Godot.Resource`) vs runtime-wrapper
architecture:

1. **Save a whole-world snapshot DTO, not live objects.** Our pure-C# `BattleSession`
   is already scene-free — a serializable mirror captured at a controlled moment is the
   same trick X2 uses with `WorldState`. Keep battle runtime engine-agnostic so a save
   never needs scene data (X2's map-sidecar coupling is the part to avoid).
2. **Persist template references + drift, not definitions.** X2's template stripping is
   our `*Data`/runtime split expressed in save format: write which `.tres` template an
   item/unit came from plus per-instance drift (HP, ammo, charges, equipped mods); rebuild
   runtime wrappers from data on load. Untouched data auto-inherits content fixes.
3. **Save only at safe boundaries.** Equivalent for us: between action steps / at turn
   boundaries in battle, at end-of-tick on the geoscape — never inside an executor action
   window.
4. **Envelope + compressed blob**: human-readable metadata header with an opaque
   compressed payload gives cheap tooling (fast save lists via partial parse) and small
   files. A metadata-header-first file layout is the safer version of their
   truncate-the-stream hack.
5. **Stable integer/Guid identities for entities**, preserved across save/load, with
   delete-all-then-restore load semantics — referential integrity stays trivial.
6. **Roster as a query, not a container**: a predicate over units ("owned by player,
   alive, at base X") prevents list-vs-truth desync. Death is a state (`KIA`/`MIA`), not
   deletion — memorial and post-game stats fall out for free.
7. **Cross-layer transfer as Guid-keyed diffs**: copy whitelisted state into battle,
   return a `MissionCombatResult`-style object (recovered items + per-combatant
   modifications) applied by an ordered pipeline — auditable, replayable, and it maps
   onto our `BattleEvent` stream philosophy.
8. **Stepped clock as the campaign spine**: research/production/funding/relations/
   autosaves all hang off ClockStep reactions; persist next-fire timestamps and the
   scheduler/timeline queue mid-flight rather than trying to reconstruct "the plan".
9. **FSM states are the persisted form of queues** — construction, aircraft builds,
   mission phases are state+progress components, not separate queue objects.
10. **Persist the RNG seed** (with an optional reseed-on-load for anti-scumming) and
    game version/commit hashes in every save; gate loads on a minimum save version.
11. **Recompute caches, never serialize them** — on load, rebuild derived indices and
    re-fire queued delayed tasks.
12. **Avoid**: reflection-heavy serialization over arbitrary object graphs (missing
    types become silent warnings). Our typed data/runtime split can push more of this to
    compile time.
