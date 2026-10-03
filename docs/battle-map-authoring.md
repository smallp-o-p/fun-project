# Battle map authoring

Use Godot 4.7.2 .NET with the **Battle box annotations** editor plugin enabled.
Restart Godot after updating this plugin to discard the previous editor session's Inspector history.
`scenes/battle/authoring/BattleMapAuthoring.tscn` is a ready-to-export map example.
`DebugMap.tscn` and `DefusalMap.tscn` are the editable sources of the existing battle-pool maps.

## Reusable assets: paint, select, annotate

1. Create a `BattleProp` or `BattleFloor` Node3D asset scene. Put its visual model below that root, independently positioned/scaled as needed.
2. Add one direct `BattleAnnotationGrid` child. Its native GridMap palette contains one translucent box marker. Standalone assets use unit-sized, centered cells and an identity grid transform.
3. Paint the asset's gameplay shape using Godot's native GridMap tools. Cell `(0,0,0)` spans the unit box starting at the asset's local origin. Visual geometry does not infer any gameplay properties.
4. Finish the shape, select exactly one occupied box with native GridMap selection, and click **Inspect selected box** in the **Box annotations** dock.
5. Edit that cell's flags in Godot's Inspector. Repeat for other individual cells, then save the reusable asset scene. Painted cells you have not annotated have no gameplay claims.
6. Instance the assets in a `BattleMapAuthoring` scene, place floors and props with native scene transforms, and click **Export BattleMap** on the map root.

The old map-paint palette and resource-dictionary authoring entry point are removed. The coordinate-keyed footprint resources remain internal serialized annotation data and a data-only bake contract; they are not a second authoring workflow. An asset without an annotation grid is decorative. Nested typed assets are each baked once.

### Fixed-layout limitation

The first cell inspection snapshots all painted coordinates, item IDs and orientations. Changing that layout blocks cell editing and export. Native Undo restores the snapshot; alternatively **Reset annotations…** asks for confirmation, clears all cell flags, and unlocks the existing painted shape. Reset itself is undoable. Flags do not follow GridMap moves, rotations or copies.

Open the reusable asset scene itself to annotate. Instance annotation overrides are unsupported; use a separate reusable asset for a different gameplay definition. The supplied spawn-floor assets demonstrate this. Native painting, erasing, layers, selection and Undo remain Godot's tools; there are no custom brushes, presets or bulk annotation controls.

## Explicit cell properties

- `HasFloor`: standing surface at the cell's bottom
- `BlocksMovement`: solid volume, independent of visibility and cover
- `BlocksLineOfSight`: blocks sight through the volume
- `BlocksVerticalLineOfSight`: blocks sight across its bottom boundary
- `WalkableTop`: standing surface one logical level above the cell
- `TopBlocksVerticalLineOfSight`: blocks sight across that upper boundary
- `CoverDirections` and `CoverAmount`: outward cover on exposed horizontal sides
- `SpawnFactionSlot`: spawn on this cell's explicit floor; -1 means none

A visible roof does not create a floor. Top-floor flags preserve a blocked interior and create a floor at Y+1. Duplicate floor claims and overlapping solid claims are errors. Spawn cells must be walkable. For roof spawns, author an explicit upper floor instead of claiming the same surface twice.

One transient footprint is built per annotation grid. Internal cover edges within that asset are suppressed. Rotating a placed asset rotates its outward sides. Contributions on the same receiving side merge by maximum. The runtime still uses one cover strength per tile: different nonzero strengths on different sides produce a diagnostic. This does not include the separate directional-strength extension or new traversal rules.

## Placement, coordinates and export

Asset roots must have unit scale and upright Y quarter-turn rotation; their inherited gameplay transforms obey the same rule. Pitch, roll and unsnapped placements are rejected. Lights, cameras and organizational nodes may be ordinary nodes, but map visual geometry belongs under a typed asset.

`GridOrigin`, square `CellWidth` and `LevelHeight` on the map root define the map-local grid. Defaults are zero origin and unit dimensions. Annotation grids must match those dimensions; the current standalone annotation editor supports unit metrics. Export never guesses how to refit art or gameplay after metric changes.

The shared coordinate contract drives picking, unit presentation, playback, path overlays, highlights and camera framing. Floor centers lie on level planes, while volume centers are half a level above them. Gameplay still uses integer cells.

Export preserves visual children and overrides, strips annotation GridMaps, and generates explicit floor-picking surfaces on collision layer 1. Art collision is moved off that layer. Annotated branches are localized in the exported scene so reloading cannot recreate markers; nested source-scene links in those branches are not retained. Scene-unique `%Name` nodes in such branches are rejected. This workflow is intended for static visual assets.

Use `proof/AnnotationBox.tscn` and `proof/AnnotationProofMap.tscn` for a minimal paint/select/annotate/reset/export exercise. Test the saved export after reloading it, not only its in-memory scene.
