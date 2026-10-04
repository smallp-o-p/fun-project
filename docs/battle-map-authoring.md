# Battle map authoring

Use Godot 4.7.2 .NET with the **Battle box annotations** editor plugin enabled.
Restart Godot after updating this plugin to discard the previous editor session's Inspector history.
`resources/maps/prop_example_map.tscn` is a baked, editable map example.
`resources/maps/debug_map.tscn` and `resources/maps/defusal_map.tscn` are both the editable scenes and the battle-pool maps; there are no separate generated copies.

## Reusable assets: paint, select, annotate

1. Create a plain `Node3D` asset scene. Put its visual model below that root, independently positioned/scaled as needed.
2. Add one direct `BattleAnnotationGrid` child. Its native GridMap palette contains three outline-only markers, with no filled faces: **Annotation box** (teal box, no default flags), **Floor** (green square at the cell bottom, `HasFloor`), and **Solid** (orange box, `BlocksMovement` and `BlocksLineOfSight`). Annotation grids use unit-sized, centered cells and an identity grid transform.
3. Paint the asset's gameplay shape using Godot's native GridMap tools. Cell `(0,0,0)` spans the unit box starting at the asset's local origin. Visual geometry does not infer any gameplay properties.
4. Floor/Solid-only shapes can be saved and baked immediately. To customize flags, finish the shape, select exactly one occupied cell with native GridMap selection, and click **Inspect selected box** in the **Box annotations** dock. Shapes containing a generic Annotation box still require first inspection before baking.
5. The first inspection of each cell fills its Inspector resource with that marker's defaults. Edit the flags, repeat for any other cells that need customization, then save the reusable asset scene. Each resource fully replaces that cell's defaults, so unchecking a default flag clears it. Cells without a resource retain their painted marker's defaults; generic boxes have no gameplay claims.
6. Instance the assets in a `BattleMapAuthoring` scene, place floors and props with native scene transforms, and click **Bake BattleMap** on the map root, then save that same scene.

The old map-paint palette and resource-dictionary authoring entry point are removed. The coordinate-keyed annotations remain internal serialized annotation data and a data-only bake contract; they are not a second authoring workflow. An asset without an annotation grid is decorative. Nested annotated assets are each baked once.

### Fixed-layout limitation

The first cell inspection snapshots all painted coordinates, item IDs and orientations. Changing that layout blocks cell editing and baking, including replacing Floor with Solid. Native Undo restores the snapshot; alternatively **Reset annotations…** asks for confirmation, clears all per-cell overrides, and unlocks the existing painted shape. The painted Floor/Solid defaults remain. Reset itself is undoable. Flags do not follow GridMap moves, rotations or copies. Baking a shortcut-only shape without inspecting any cells does not lock its layout.

Open the reusable asset scene itself to annotate. Instance annotation overrides are unsupported; use a separate reusable asset for a different gameplay definition. Native painting, erasing, layers, selection and Undo remain Godot's tools. Floor and Solid are simple paintable shortcuts, not presets for every flag combination; there are no custom brushes or bulk annotation controls.

## Explicit cell properties

- `HasFloor`: standing surface at the cell's bottom
- `BlocksMovement`: solid volume, independent of visibility and cover
- `BlocksLineOfSight`: blocks sight through the volume
- `BlocksVerticalLineOfSight`: blocks sight across its bottom boundary
- `WalkableTop`: standing surface one logical level above the cell
- `TopBlocksVerticalLineOfSight`: blocks sight across that upper boundary
- `CoverDirections` and `CoverAmount`: outward cover on exposed horizontal sides

A visible roof does not create a floor. Top-floor flags preserve a blocked interior and create a floor at Y+1. Duplicate floor claims and overlapping solid claims are errors. Existing deployment positions live in the map root’s `SpawnSlots` dictionary (cell → faction slot), separate from reusable assets. Spawn cells must be walkable. Player/enemy spawn-floor flags are deferred.

Floor and Solid set only the defaults listed above. Neither shortcut supplies cover, vertical sight blocking, a walkable top, roof flags, or spawn assignments; set those explicitly when needed. A Solid marker does not supply a floor beneath itself.

One transient footprint is built per annotation grid. Internal cover edges within that asset are suppressed. Rotating a placed asset rotates its outward sides. Contributions on the same receiving side merge by maximum. The runtime still uses one cover strength per tile: different nonzero strengths on different sides produce a diagnostic. This does not include the separate directional-strength extension or new traversal rules.

## Placement, coordinates and baking

Asset roots must have unit scale and upright Y quarter-turn rotation; their inherited gameplay transforms obey the same rule. Pitch, roll and unsnapped placements are rejected. Unannotated visual geometry, lights, cameras and organizational nodes are decorative.

The board uses fixed unit cells at world origin. Keep the map root at identity transform; place and rotate reusable assets beneath it. Annotation grids use unit Cell Size.

Floor centers follow the existing board convention `(x + 0.5, y, z + 0.5)`. Annotation volume centers are half a unit above their floor.

**Bake BattleMap** updates the stored `MapData` and replaces the generated `FloorPicking` child in place. It preserves visual instances and scene-unique names, saves art-collision overrides off picking layer 1, and generates explicit picking surfaces on that layer. `FloorPicking` is reserved for generated geometry. Save the scene after baking. After changing assets, placements or spawn assignments, bake again before saving/running; there is no automatic bake or runtime recomputation.

At runtime, the map reads its stored data and removes annotation GridMaps. Reusable asset scenes retain their annotations for later editing.

Use `scenes/battle/authoring/proof/AnnotationBox.tscn` and `AnnotationProofMap.tscn` for a minimal paint/select/annotate/reset/bake exercise. Verify the saved map after reloading it.
