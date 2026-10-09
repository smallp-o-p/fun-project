# Xenonauts 2 — Save System & Game State Research

Historical evidence only. Claims and paths describe the recorded baseline or external checkout; they are not current implementation requirements.

This document records save-system research from the decompiled Xenonauts 2 source (`.xenonauts2-research/`, gitignored). The takeaways are proposals for this game's future persistence layer.

File paths are relative to `.xenonauts2-research/Xenonauts2/Assets/Code/`. Key claims were spot-checked against that source.

## TL;DR

X2's save is a full ECS world dump in one file: a readable JSON metadata envelope (name, geoscape date, playtime, difficulty, game version + commit hashes, compressed screenshot) wrapping an opaque `worldData` blob (FullSerializer `fsData` → Odin binary → zip → Base64). Soldiers, bases, research, and UFOs are entities in the Artitas ECS world. Saving snapshots the complete world. Authored definitions (templates) live outside the save as addressable assets; the save stores instances plus drift. Load deletes every entity and rebuilds the world verbatim.

## Persistence mechanics

- A `SaveGameRequest` world event (`Screens/Common/Events/8SaveGameEvents.cs`) is enqueued by `SaveGameSystem.QueueSaveGameRequest` (`Screens/Common/Saving/SaveGameSystem.cs`). Screen-specific subclasses drain the queue only at safe boundaries: strategy saves at end of geoscape tick; ground combat saves in dedicated phase steps under a game-lock tag, async on a thread pool (mid-battle saving is supported). Modal UI, tutorial, game over, and IronMan block quicksave.
- `InternalProcessSaveGameRequest` fires `PreSaveGameReport` (systems scrub state — the RNG is serialized into a `GlobalRNG` singleton entity), then serializes a copied `WorldState`, attaches metadata, and hands the asset to the content pipeline (`ContentManager/Tasks/SaveProcessTask.cs`: versioned-asset conversion → FullSerializer → compressed JSON → file write).
- Format: one JSON file per save. `worldData` and the screenshot are zip+Base64 strings inside the JSON (`SaveGameConverter.cs`). The payload is unencrypted. Saves live under `My Documents/My Games/Xenonauts2/Saves/` (Dropbox/OneDrive paths refused) and name directories and files from `SaveGameGroup` identity segments.
- ECS serialization: `WorldState` stores alive entities plus a component bag per type, indexed by entity ID. Entity references serialize as bare integer IDs and are recreated on load. Template stripping: a component identical to its authored template is omitted from the save and re-instantiated from the template asset on load, so untouched data auto-inherits content fixes. Load is `DeleteAllEntities()` → wholesale bag copy → composition/family recompute → template re-instantiation → link restoration → cache rebuild.
- Versioning: `SaveGame.CanLoad()` gates on a minimum version; missing component types deserialize as null (warning-only unless referenced); `TypeReplacement` remaps renamed types; legacy formats are supported.
- Load flow: menu listing uses partial loading — the JSON stream is truncated at the `,"worldData":` marker and given a synthetic postfix, so the save list reads only the metadata prefix (`AbstractSaveLoadMenuElement.cs`, `PartiallyLoadableAssetParameters`).
- Ground-combat saves additionally store `map` (an `ArtitasScene` asset reference) and `gcParameters` so the tactical scene can be rebuilt; variant maps re-run generation from stored parameters. The map remains a referenced asset.

## Soldier roster storage

A soldier is an entity conforming to `StrategyArchetypes.StrategyActor` (`Screens/Strategy/Factories/StrategyArchetypes.cs`), composed from layered archetypes:

- `SharedCombatantDefinition` (`Screens/XenonautsArchetypes.cs`): per-stat components — hit points, time units, accuracy, bravery, defence, reflexes, strength, psionics, morale, stress, resistances, medals, injuries with per-injury recovery times. Each stat is a `RangeComponent` (min/value/max) so growth can raise the max.
- Biography: name, portrait layers, date of birth, nationality, flag, regiment, data-driven rank, mission history.
- Progression: per-mission XP, career points per attribute with escalating thresholds, history.
- Equipment: the entity is an inventory — real item entities linked into slots.
- Roles share the archetype: soldier/scientist/engineer differ by combat/scientific/technical rating; `IsSoldier()` is `CombatRating > 0` (`StrategyActorEntityExtensions.cs`).

Each screen builds the roster with a cached `Family` filtering strategy actors by ownership, base, and role. Lifecycle is an explicit `ActorAssignment` state machine (`Hireable → Available → OnMission → Recovering / KilledInAction / MissingInAction / Fired`). KIA/MIA entities persist in the world and each save. The memorial UI reads those entities.

Strategy ↔ ground-combat transfer (`StrategyToGCMissionStartParameters.cs`): whitelisted components are cloned onto the GC combatant template, keeping the strategy Guid. The return trip carries a `MissionCombatResult` (recovered items + per-combatant diff-templates keyed by `GUIDReferenceComponent` + campaign modifications), applied by an ordered pipeline (`CombatMission.cs`). Progression converts per-mission XP into career progress, then progress into stat gains via `floor(progress / threshold)` with escalating thresholds; rank is derived from total gains.

## What campaign state is persisted

- **Whole-world snapshot**: entities for clock (with per-step last-fire stamps), difficulty, money and funding history, regions, research projects and queues, manufacturing queues and stockpiles, bases and buildings, personnel and hiring pools, UFOs and missions (FSM states and progress mid-flight), interceptors, victory counters, and the RNG seed. Ground-combat saves add the map asset reference and mission parameters.
- **Reconstructed or loaded from assets**: system caches, families, pause/time-speed state, UI state, authored definitions, and tactical map geometry.
- **Autosave policy**: every N in-game days; before entering combat; after combat resolution; on game over. IronMan is one slot plus hidden backups.

## Takeaways for our architecture

The persistence layer is deferred. These proposals guide future work.

1. Capture a serializable world-state snapshot at a controlled moment. Use the scene-independent `BattleSession` as the tactical source.
2. Persist template references plus per-instance drift (health, ammo, charges, mods) and rebuild runtime wrappers from authored data on load — the save-format expression of our Data/Runtime split.
3. Save at settled battle-action or turn boundaries, and at the end of a geoscape tick. Complete the executor action window first.
4. A readable metadata envelope plus an opaque compressed payload gives cheap tooling (fast save lists via partial parse). A metadata-header-first layout is the safer version of X2's truncate-the-stream trick.
5. Keep stable entity identities across save/load with delete-all-then-restore semantics; referential integrity stays trivial.
6. Query rosters from entity state. Retain dead entities for memorials and post-game statistics.
7. Return cross-layer results as identity-keyed diffs applied by an ordered pipeline — auditable, replayable, and a natural fit for our `BattleEvent` stream philosophy.
8. Hang research, production, funding, and autosaves off stepped-clock reactions; persist next-fire timestamps and pending schedules mid-flight for restoration on load.
9. Persist the RNG seed (optional reseed-on-load for anti-scumming) and version/commit hashes; gate loads on a minimum save version.
10. Recompute caches on load.
11. Use typed serialization contracts that report missing types explicitly.
