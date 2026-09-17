# Authoring battle maps

Battle maps are authored in the Godot editor on a **`GridMap`** and baked to a `BattleMapData`
`.tres` that `BattleMapData.CreateBoardState()` turns into a `BattleBoardState`. The GridMap is
Godot's native 3D tiler, so its cells line up 1:1 with the board's `Vector3I` grid and you see
the real meshes in 3D while you author.

## Pieces

- **`TileBrushData`** (`scripts/battle/map`) — per-tile gameplay template: `Walkable`,
  `BlocksLineOfSight`, `CoverDirections` + `CoverAmount` (amount editable only when a direction
  is set), `SpawnFactionSlot` (-1 = none). All independent — a wall with no cover, or walkable
  smoke that blocks LOS, are both legal. Cover is never inferred.
- **`BattleMapAuthoring`** (`scenes/battle/authoring`) — a `[Tool]` node that **extends
  `GridMap`**; you paint terrain directly on it. Its `MeshLibrary` supplies the terrain meshes;
  the `Brushes` palette (`Dictionary<StringName, TileBrushData>`) maps each MeshLibrary item
  **name** to a gameplay brush. The **Bake to BattleMapData** button reads the painted cells and
  writes `TargetPath`.
- **`BattleMapBaker` / `MapDeployment` / `MapSpawnZoneData`** — pure C# (`scripts/battle/map`),
  no Godot loading, unit-tested.

## Coordinate mapping

A `GridMap` cell `Vector3I(x, y, z)` is already board space: **X = width** (East = +X),
**Y = elevation/level**, **Z = depth** (South = +Z), consistent with the engine's `North = -Z`.
The baker normalizes painted cells to a `(0,0,0)` origin and sets `AuthoredTilesAreComplete =
true`, so any unpainted cell is a hole.

## Editor setup (one-time)

1. Build a **`MeshLibrary`** with one item per terrain type; name items meaningfully
   (e.g. `floor`, `wall`, `cover_n`, `spawn_a`).
2. Author a `TileBrushData` `.tres` for each meaning:
   - floor = `Walkable`
   - wall = `!Walkable` + `BlocksLineOfSight` (cover stays `None` ⇒ zero cover)
   - cover = `Walkable` + `CoverDirections` + `CoverAmount`
   - spawn = a floor brush + `SpawnFactionSlot = N`
3. Create a scene with a **`BattleMapAuthoring`** (GridMap) node; assign the `MeshLibrary`;
   fill `Brushes` (item name → brush `.tres`); set `TargetPath`.

## Workflow

- **Create:** paint the 3D grid, click **Bake**.
- **Edit:** reopen the scene, repaint, re-bake.

The `.tscn` is the source of truth; the baked `.tres` is a build artifact. A painted cell whose
MeshLibrary item name isn't in `Brushes` bakes as a plain walkable floor.

## Spawning from a baked map

`BattleSetupResolver.Resolve(type, seed?, playerDeployment?)` chooses the map and
builds ordered sides. `MapDeployment.AssignSpawns(map, slot, loadouts)` pairs each
side's roster directly with that slot's cells, sorted X/Y/Z. The slot is the
side's index in `BattleTypeData.Factions`; roster order is preserved.

The resolved `BattleSetup` groups each faction with its objectives and positioned
units. `BattleFactory.Start(setup)` creates a fresh board, checks all placements,
registers systems, and submits the spawn, object-placement, and start actions.
An out-of-bounds or blocked cell is a cell failure; insufficient slot capacity
is `SpawnSlotShortfall`.

A supplied `PlayerDeployment` retains its campaign faction and combatants. It
replaces the player roster while keeping that side's authored objectives and
spawn slot. Startup never reassigns a combatant's owning faction.
