# Authoring battle maps

Use the Godot editor with the **Battle box annotations** plugin enabled. Annotate an entity, export its mesh and footprint, then use the entity in a map palette.

`BattleMapAuthoring` paints native GridMaps, bakes `BattleMapData`, and exports a `BattleMap` scene.

## Resources and nodes

The authoring types live in `scenes/battle/authoring/`:

- `BattleEntityAnnotator`: a `Node3D` with one `MeshInstance3D` child named `Model` and a plain `Annotations` GridMap.
- `BattleAnnotationMarker`: a brush mesh with a `Defaults` resource stored as Mesh metadata named `footprint_data`.
- `BattleFootprintData`: cell flags for floors, movement blocking, sight blocking, and cover.
- `BattleProp`: an exported mesh with a `battle_footprint` dictionary mapping `Vector3I` cells to `BattleFootprintData`.
- `BattleMapAuthoring`: a `BattleMap` subclass with native `Floors` and `Props` GridMaps, a `Palette` MeshLibrary, and `SpawnSlots`.
- `BattleMap`: the runtime scene root, with `MapData` and owned `FloorTiles` and `PropTiles` GridMaps.

`BattleMapData` and `BattleMapTileData` in `scripts/battle/map/` store the baked dimensions and cell data.

## Brushes

`scenes/battle/authoring/markers/MarkerMeshlib.tscn` holds the brush meshes. `MarkerPalette.tres` in that folder supplies the brush MeshLibrary.

Save brush scenes after editing their defaults. Update the MeshLibrary through **Update from Scene**, with transforms enabled.

Updates match items by node name. Preserve names and IDs used by painted cells: 0 = Annotation box, 1 = Floor, 2 = Solid.

The Floor brush sets `HasFloor`. The Solid brush sets movement and sight blocking. Author cover and vertical sight-blocking flags explicitly.

## Annotate and export an entity

1. Save a copy of `scenes/battle/authoring/BattleEntityAnnotator.tscn`.
2. Assign one mesh to its childless `Model` node.
3. Assign the brush MeshLibrary to `Markers`.
4. Paint unit cells on the `Annotations` GridMap.
5. Select one painted cell and click **Inspect selected box** in the **Entity annotations** dock.
6. Set `ExportPath` to a separate scene path.
7. Click **Export BattleProp**.
8. Save the editable source scene.

The `Annotations` GridMap uses an identity transform, centered unit cells, and Cell Scale 1. Painted cells support upright Y quarter-turn orientations. Cell `(0,0,0)` spans `(0,0,0)` to `(1,1,1)`.

First inspection copies the brush's authored footprint resource. The coordinate override replaces every brush flag, including false values. Erased cells retain their overrides for repainting. Export includes the currently painted cells.

**Reset annotations** clears all overrides after confirmation and supports Undo. Uninspected cells use brush defaults. Missing brush metadata rejects export.

Export preserves the model transform and active materials. Mesh metadata stores the resolved footprint so it survives native MeshLibrary export.

## Tile palette

`resources/map_entities/TilePalette.tscn` contains finished props as mesh children. Set each instance position to the exported model position minus `(0.5, 0.5, 0.5)`. This aligns annotation coordinates with centered GridMap cells.

After re-exporting an entity, update `resources/map_entities/MapPalette.tres` through **Update from Scene**, with transforms enabled. Preserve item names and IDs: FloorTile 0, Block 1, Car 2.

Native export carries mesh materials, transforms, footprint metadata, and authored collision into the palette.

## Paint, bake, and export a map

1. Save a copy of `scenes/battle/authoring/BattleMapAuthoring.tscn`.
2. Assign the tile MeshLibrary to `Palette`.
3. Paint ground items in `Floors`.
4. Paint obstacles and decorations in `Props`.
5. Set `SpawnSlots` to map cells to faction slots.
6. Click **Bake BattleMap**.
7. Save the authoring scene to retain its `MapData`.
8. Set `ExportPath` for the runtime scene.
9. Click **Export BattleMap**.

Each placement expands the item's whole footprint. Both anchor layers use Y=0. Upright Y quarter-turns rotate meshes and cover directions together.

Bake reads the assigned palette and marks the scene unsaved. Export bakes again, then saves a separate `BattleMap` with owned GridMaps and their serialized palette.

After editing an entity, export it, update the map palette, then rebake affected maps.

## Bake requirements

- Each expanded gameplay cell needs a finite, grid-aligned center and nonnegative X/Y/Z coordinates.
- Each prop volume cell needs floor support at its projected X/Z coordinate, including elevated volumes.
- Floor claims must be unique. Prop volumes must be disjoint, including volumes with clear blocking flags.
- Ground-level props replace floor flags and retain `HasFloor`.
- Maps support one walkable ground layer. `WalkableTop` and elevated `HasFloor` reject baking.
- Each spawn needs a walkable floor and a nonnegative faction slot.
- Every palette item needs footprint metadata.
- Map roots and GridMaps use identity transforms, centered unit cells, and Cell Scale 1.

The baker suppresses internal cover edges and rotates external cover with placement. Different strengths on separate sides of a cell require the deferred directional-cover extension.

Dimensions use the maximum coordinate plus one per axis. Runtime consumes the saved `MapData` and GridMaps directly.

## Collision

Author floor-picking collision in the palette source scene. Add a `StaticBody3D` with `CollisionShape3D` children under the finished prop mesh. Place shapes at walkable surfaces and leave holes clear.

`Floors` uses collision layer 1. `Props` uses layer 2. Floor-picking rays target layer 1. Battle pointer rays include layers 1–3: layer 2 props occlude layer 3 unit hitboxes. The palette source includes fitted Block and Car collision boxes so re-exporting meshes preserves pointer occlusion.

`HasFloor` defines gameplay support. Collision comes from the authored shapes. Keeping those shapes in the palette source preserves them when entity re-export replaces its finished scene.

## Spawns and startup

`BattleSetupResolver` selects a scene from `BattleTypeData.MapPool` using the seed. A wrong root or missing `MapData` returns `MapSceneInvalid`.

`MapDeployment.AssignSpawns` pairs each side's loadouts with its slot's cells in X/Y/Z order. Insufficient capacity returns `SpawnSlotShortfall`.

See [tile semantics](battlescape-tile-system.md) and [battle startup](battlescape-architecture.md).
