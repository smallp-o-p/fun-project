# Authoring battle maps

Battle maps are authored in the Godot editor on a `GridMap` and exported as a `PackedScene` rooting a `BattleMap` node. The scene stores the painted cells, the `MeshLibrary`, a `BattleTilePalette`, and a `BattleMapData` resource. `BattleSetupResolver` reads the `MapData` from pooled scenes; `BattleFactory.Start` builds the runtime board from it.

## Pieces

- **`BattleMapTileData`** (`scripts/battle/map`) — the per-tile gameplay template: `Walkable` (default true), `BlocksLineOfSight`, `BlocksVerticalLineOfSight`, `CoverDirections` plus `CoverAmount` (editable only when a direction is set), and `SpawnFactionSlot` (-1 = none). All fields are independent; cover is never inferred.
- **`BattleTilePalette`** (`scenes/battle/authoring`) — a `Resource` mapping each `MeshLibrary` item **name** to a `BattleMapTileData` brush. Assigning a `MeshLibrary` auto-fills `Brushes` with a default brush per item name. Do not invent names the library lacks; the editor reports them as errors.
- **`BattleMapAuthoring`** (`scenes/battle/authoring`) — a `[Tool]` node that extends `GridMap`; paint terrain on it directly. Its `Palette` export supplies the mesh library.
- **`BattleMap`** — a `GridMap` subclass with `MapData` and `UsedPalette` exports; the root of the exported scene.
- **`BattleMapData`** (`scripts/battle/map`) — a `Resource` with `Dimensions` and a `Tiles` dictionary (`Vector3I` → `BattleMapTileData`).
- **`MapDeployment`** (`scripts/battle/map`) — pure C# spawn pairing, unit-tested.

## Export

Set the `TargetPath` export and click **Export BattleMap**. The button packs and saves a `PackedScene` whose root is one `BattleMap` node named `Map`, carrying:

- the authored `MeshLibrary` and grid settings (`CellSize`, `CellCenterX/Y/Z`, `Scale`)
- the painted cells and their orientations
- the built `MapData` and the `UsedPalette`

The default `TargetPath` ends in `.tres`, but the output is a scene. Set a scene path such as `res://resources/maps/my_map.tscn`.

Export rules:

1. Every painted cell must have non-negative X, Y, and Z. Export stops otherwise.
2. Every painted cell's mesh item name must exist in `Brushes`. A missing mapping stops the export; there is no default floor fallback.
3. Dimensions are computed from the painted X/Z range (`max − min + 1`) and height (`maxLevel + 1`). Tile keys keep their painted coordinates. Paint from X = 0 and Z = 0 so tiles align with the computed dimensions.

## Coordinate mapping

A `GridMap` cell `Vector3I(x, y, z)` is already board space: X = width (east is +X), Y = elevation, Z = depth (south is +Z). This matches the engine's `North = -Z`.

## Board conversion

`new BattleBoardState(mapData)` builds the runtime board. Every cell starts unwalkable, then each authored tile applies its data. A cell without an authored tile is a hole: unwalkable and unreachable. An authored tile outside `Dimensions` logs an error and is skipped. Tile semantics: [battlescape-tile-system.md](battlescape-tile-system.md).

## Spawning from a baked map

`BattleSetupResolver.Resolve(type, seed?, playerDeployment?)` picks a map from `BattleTypeData.MapPool` with the seed. Pool entries are `PackedScene`s rooting a `BattleMap`; a wrong root or missing `MapData` fails with `MapSceneInvalid`. `MapDeployment.AssignSpawns(map, slot, loadouts)` pairs loadouts with that slot's cells in X/Y/Z order. The slot is the side's index in the faction order; roster order is preserved. Too few cells fail with `SpawnSlotShortfall`. Cell validity is checked later, in `BattleFactory.Start`.

The resolved `BattleSetup` groups each faction with its objectives and positioned units. `BattleFactory.Start(setup)` builds a fresh board, validates every placement, registers declared systems, and submits the spawn, object-placement, and start actions. A supplied `PlayerDeployment` replaces the player roster with the campaign faction and combatants, and keeps that side's authored objectives and spawn slot. Startup never reassigns a combatant's owning faction; a mismatch is a typed failure. Startup detail: [battlescape-architecture.md](battlescape-architecture.md).
