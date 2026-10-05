# Xenonauts 2 — Save System & Game State Research

Research findings from the decompiled Xenonauts 2 source (`.xenonauts2-research/`, gitignored), recorded to inform our save/persistence design. This document describes Xenonauts 2, not this game's current design; the "Takeaways" section maps findings onto our architecture as design inputs only — none of it is implemented here.

File paths are relative to `.xenonauts2-research/Xenonauts2/Assets/Code/`. Key claims were spot-checked against that source.

## TL;DR

X2's save is a full ECS world dump in one file: a readable JSON metadata envelope (name, geoscape date, playtime, difficulty, game version + commit hashes, compressed screenshot) wrapping an opaque `worldData` blob (FullSerializer `fsData` → Odin binary → zip → Base64). There are no per-subsystem save DTOs and no soldier DTOs — soldiers, bases, research, and UFOs are all entities in the Artitas ECS world, and saving snapshots the whole thing. Authored definitions (templates) live outside the save as addressable assets; the save stores instances plus drift. Load deletes every entity and rebuilds the world verbatim.

## Persistence mechanics

- A `SaveGameRequest` world event (`Screens/Common/Events/8SaveGameEvents.cs`) is enqueued by `SaveGameSystem.QueueSaveGameRequest` (`Screens/Common/Saving/SaveGameSystem.cs`). Screen-specific subclasses drain the queue only at safe boundaries: strategy saves at end of geoscape tick; ground combat saves in dedicated phase steps under a game-lock tag, async on a thread pool (mid-battle saving is supported). Modal UI, tutorial, game over, and IronMan block quicksave.
- `InternalProcessSaveGameRequest` fires `PreSaveGameReport` (systems scrub state — the RNG is serialized into a `GlobalRNG` singleton entity), then serializes a copied `WorldState`, attaches metadata, and hands the asset to the content pipeline (`ContentManager/Tasks/SaveProcessTask.cs`: versioned-asset conversion → FullSerializer → compressed JSON → file write).
- Format: one JSON file per save. `worldData` and the screenshot are zip+Base64 strings inside the JSON (`SaveGameConverter.cs`). No encryption. Saves live under `My Documents/My Games/Xenonauts2/Saves/` (Dropbox/OneDrive paths refused) and name directories and files from `SaveGameGroup` identity segments.
- ECS serialization: `WorldState` stores alive entities plus a component bag per type, indexed by entity ID. Entity references serialize as bare integer IDs and are recreated on load. Template stripping: a component identical to its authored template is omitted from the save and re-instantiated from the template asset on load, so untouched data auto-inherits content fixes. Load is `DeleteAllEntities()` → wholesale bag copy → composition/family recompute → template re-instantiation → link restoration → cache rebuild.
- Versioning: `SaveGame.CanLoad()` gates on a minimum version; missing component types deserialize as null (warning-only unless referenced); `TypeReplacement` remaps renamed types; legacy formats are supported.
- Load flow: menu listing uses partial loading — the JSON stream is truncated at the `,"worldData":` marker and given a synthetic postfix, so only the metadata prefix parses and the world blob is never deserialized for the save list (`AbstractSaveLoadMenuElement.cs`, `PartiallyLoadableAssetParameters`).
- Ground-combat saves additionally store `map` (an `ArtitasScene` asset reference) and `gcParameters` so the tactical scene can be rebuilt; variant maps re-run generation from stored parameters. The map itself is not world state.

## Soldier roster storage

There is no roster class and no soldier class. A soldier is an entity conforming to `StrategyArchetypes.StrategyActor` (`Screens/Strategy/Factories/StrategyArchetypes.cs`), composed from layered archetypes:

- `SharedCombatantDefinition` (`Screens/XenonautsArchetypes.cs`): per-stat components — hit points, time units, accuracy, bravery, defence, reflexes, strength, psionics, morale, stress, resistances, medals, injuries with per-injury recovery times. Each stat is a `RangeComponent` (min/value/max) so growth can raise the max.
- Biography: name, portrait layers, date of birth, nationality, flag, regiment, data-driven rank, mission history.
- Progression: per-mission XP, career points per attribute with escalating thresholds, history.
- Equipment: the entity is an inventory — real item entities linked into slots.
- Roles share the archetype: soldier/scientist/engineer differ by combat/scientific/technical rating; `IsSoldier()` is `CombatRating > 0` (`StrategyActorEntityExtensions.cs`).

The roster is a query, not a container: each screen builds a cached `Family` filtering strategy actors by ownership, base, and role. Lifecycle is an explicit `ActorAssignment` state machine (`Hireable → Available → OnMission → Recovering / KilledInAction / MissingInAction / Fired`). Dead soldiers are not deleted — KIA/MIA entities persist in the world and every save, which feeds the memorial UI.

Strategy ↔ ground-combat transfer (`StrategyToGCMissionStartParameters.cs`): whitelisted components are cloned onto the GC combatant template, keeping the strategy Guid. The return trip carries a `MissionCombatResult` (recovered items + per-combatant diff-templates keyed by `GUIDReferenceComponent` + campaign modifications), applied by an ordered pipeline (`CombatMission.cs`). Progression converts per-mission XP into career progress, then progress into stat gains via `floor(progress / threshold)` with escalating thresholds; rank is derived from total gains.

## What campaign state is persisted

- **Whole-world snapshot**: entities for clock (with per-step last-fire stamps), difficulty, money and funding history, regions, research projects and queues, manufacturing queues and stockpiles, bases and buildings, personnel and hiring pools, UFOs and missions (FSM states and progress mid-flight), interceptors, victory counters, and the RNG seed. Ground-combat saves add the map asset reference and mission parameters.
- **Not persisted** (re-derived): system caches, families, pause/time-speed state, UI state, authored definitions, and tactical map geometry.
- **Autosave policy**: every N in-game days; before entering combat; after combat resolution; on game over. IronMan is one slot plus hidden backups.

## Takeaways for our architecture

Design inputs, not current implementation — our save/persistence layer does not exist yet.

1. Save a whole-world snapshot at controlled moments, not live objects. Our `BattleSession` is already scene-free; keep it that way so a save never needs scene data (X2's map-sidecar coupling is the part to avoid).
2. Persist template references plus per-instance drift (health, ammo, charges, mods) and rebuild runtime wrappers from authored data on load — the save-format expression of our Data/Runtime split.
3. Save only at safe boundaries: between action steps or at turn boundaries in battle, at end-of-tick on the geoscape — never inside an executor action window.
4. A readable metadata envelope plus an opaque compressed payload gives cheap tooling (fast save lists via partial parse). A metadata-header-first layout is the safer version of X2's truncate-the-stream trick.
5. Keep stable entity identities across save/load with delete-all-then-restore semantics; referential integrity stays trivial.
6. Model rosters as queries, not containers, and treat death as a state, not deletion — memorial and post-game statistics fall out for free.
7. Return cross-layer results as identity-keyed diffs applied by an ordered pipeline — auditable, replayable, and a natural fit for our `BattleEvent` stream philosophy.
8. Hang research, production, funding, and autosaves off stepped-clock reactions; persist next-fire timestamps and pending schedules mid-flight rather than reconstructing the plan.
9. Persist the RNG seed (optional reseed-on-load for anti-scumming) and version/commit hashes; gate loads on a minimum save version.
10. Recompute caches on load; never serialize them.
11. Avoid reflection-heavy serialization over arbitrary object graphs — missing types become silent warnings. Our typed data/runtime split can push more of this to compile time.
