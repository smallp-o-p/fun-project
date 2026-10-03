# Battle map authoring

Use Godot 4.7.2 .NET with the **Battle box annotations** plugin enabled.

## Brushes

`scenes/battle/authoring/markers/MarkerMeshlib.tscn` contains the brush meshes.
Each `BattleAnnotationMarker` is one MeshInstance3D with a `Defaults`
BattleFootprintData resource. The same resource is stored on its **Mesh** as
`footprint_data` metadata, so edits to default fields survive native export.
Save the brush scenes before updating the MeshLibrary.

`MarkerPalette.tres` is an ordinary MeshLibrary assigned to an annotator's
**Markers** property. Use Godot's MeshLibrary **Update from Scene** with transforms
applied. Existing entries are matched by node name: preserve names/IDs used by
painted cells. The supplied IDs remain 0 (Annotation box), 1 (Floor), and 2 (Solid),
even when their order in the source scene changes. Do not clear and rebuild a
palette that already has painted users.

## Annotate and export an entity

1. Open `scenes/battle/authoring/BattleEntityAnnotator.tscn` and save a copy.
2. Assign one mesh to **Model**, a MeshInstance3D. Its transform and active materials
   are preserved at export. The tool does not compile branches of meshes.
3. Assign the brush MeshLibrary on the root. Paint unit cells in the native
   **Annotations** GridMap. Cell `(0,0,0)` spans the box from `(0,0,0)` to `(1,1,1)`.
4. Select one painted cell and choose **Inspect selected box** in **Box annotations**.
   First inspection copies brush defaults; explicit flags replace all defaults,
   including false values. Erased cells retain stored overrides but are excluded
   from export. Repainting their coordinates applies the overrides again.
   **Reset annotations** clears overrides with confirmation and supports Undo.
5. Choose a separate **Export Path** and click **Export BattleProp**. Save the editable
   source too. Exported props contain the finished mesh and a resolved
   Vector3I-to-BattleFootprintData dictionary in the Mesh's `battle_footprint`
   metadata. Native export strips custom node properties, so the Mesh owns this data.

The supplied Floor and Solid brushes set only HasFloor and movement/sight blocking
respectively. Geometry never infers flags. Cover directions/amount, bottom and top
vertical sight blocking, and other cell flags remain explicit. Current maps reject
WalkableTop and elevated HasFloor because there is one walkable ground layer.

## Tile palette

`resources/map_entities/TilePalette.tscn` instances the finished props as mesh
children of a Node3D. Each instance subtracts `(0.5,0.5,0.5)` from the exported model
position to align the entity's corner-based annotation coordinates with GridMap's
centered cells. Keep that placement when replacing a finished model.

`resources/map_entities/MapPalette.tres` is an ordinary MeshLibrary. Use its native
**Update from Scene**, applying transforms, after re-exporting entities. This keeps
existing item names/IDs (FloorTile 0, Block 1, Car 2). Material slots, mesh transforms,
footprint metadata and floor-picking collision travel through native export.
There are no custom ItemId fields, folder scans, or recursive mesh compiler.

## Paint and bake a map

1. Open `scenes/battle/authoring/BattleMapAuthoring.tscn` and save a copy.
2. Assign the tile MeshLibrary to **Palette**. Floors and Props immediately use it.
3. Paint ground items in **Floors** and obstacles/decorations in **Props**. Each
   placement expands the item's whole footprint. Both anchor layers remain at Y=0;
   upright Y quarter-turns rotate the mesh and cover directions together.
4. Set **Spawn Slots** (cell to faction slot); each spawn needs a walkable floor.
5. Click **Bake BattleMap** and save to retain data in the editable scene. To make a
   separate runtime scene, select **Export Path** and click **Export BattleMap**.
   Export includes owned Floors/Props GridMaps and their serialized palette.

Bake reads the assigned palette only. After editing an entity, export it, update
its native palette, then rebake affected maps. Missing footprint metadata produces
an authoring error rather than silently discarding gameplay data.

Every prop volume cell, including elevated volumes, needs floor support at its
projected X/Z coordinate. Duplicate floor claims and overlapping prop volumes are
rejected even when their blocking flags are false. At ground level, prop flags
replace floor flags while preserving the floor's existence. Higher prop volumes
never become additional walkable floors. Internal cover edges are suppressed;
external cover rotates with placement. Conflicting cover strengths on different
sides still require the separate directional-cover extension.

Map roots and GridMaps use identity transforms, centered unit cells and Cell Scale 1.
Floors use collision layer 1 for native picking; Props use layer 2 so they do not
intercept floor rays. Exported floor shapes follow the annotated footprint.
Runtime uses saved MapData and native GridMaps without palette loading or rebaking.
