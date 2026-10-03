# Battle map authoring

Open `scenes/battle/authoring/BattleMapAuthoring.tscn` in Godot 4.7.2 .NET. This ready-to-export example contains floor instances and a two-cell car in one scene. `DebugMap.tscn` and `DefusalMap.tscn` in the same folder are the editable sources for the existing battle-pool maps.

## Reusable typed pieces

Create a `BattleFloor` or `BattleProp` Node3D root and put its visual model below it. Both expose the same `BattleFootprintData` resource. Its `Cells` dictionary maps local integer cell offsets to `BattleFootprintCellData` resources. Each cell is one tile-sized box; compose them to describe a car, an L-shaped object or stacked volumes. An empty definition is an explicitly decorative prop.

A piece's origin is a local grid corner. Cell `(0,0,0)` spans one cell width/depth from that corner, with its bottom on the level plane. The model can be independently positioned/scaled beneath the piece. Move and rotate the typed root to place the complete asset. Only upright 90-degree turns and unscaled gameplay transforms are supported; inherited transforms must obey the same rule.

The **Battle footprint outlines** plugin draws editor-only wireframes for typed assets. It reads the definitions directly; there are no persistent box nodes or rectangle-painting tools. Edit the resource dictionary in the Inspector, and use ordinary scene instancing and transform snapping for placement. Shared resource edits update the reusable definition; use Godot's **Make Unique** when an instance needs its own data.

Lights, cameras and organizational nodes can remain ordinary nodes. Visual geometry must be inside a typed floor/prop asset. Nested typed assets are each baked once.

## Cell properties

- `HasFloor` explicitly contributes a standing surface at the bottom of this cell
- `BlocksMovement` prevents standing in this volume, independently of LOS and cover
- `BlocksLineOfSight` blocks sight through the cell's volume
- `BlocksVerticalLineOfSight` blocks crossing the cell's bottom boundary
- `WalkableTop` contributes a standing surface one logical level above this cell
- `TopBlocksVerticalLineOfSight` independently blocks crossing that upper boundary
- `CoverDirections` and `CoverAmount` describe outward cover on exposed horizontal sides; rotating the piece rotates those sides
- `SpawnFactionSlot` applies to this cell's `HasFloor` surface; leave it at -1 for no spawn. For a roof spawn, use an explicit floor cell there rather than also marking a duplicate top face

An upper box can block space without having a floor beneath it. A visible flat roof never creates a floor by itself. A marked top keeps the object interior blocked and creates the floor at `Y+1`. Duplicate floor claims are errors, including an explicit floor and a top face that claim the same surface.

The baker suppresses internal cover edges between cells of the same asset. Contributions on the same receiving side merge by maximum. Until PR #4's independent side-strength extension lands, different nonzero strengths on separate sides of one standing cell produce a diagnostic instead of being flattened into the current single-strength backend representation.

## Shared grid metrics

Set `GridOrigin`, `CellWidth` and `LevelHeight` on the map root. X/Z cells are square; width and height default to 1. Definitions use these metrics. Asset placement must be snapped/aligned to the chosen map grid; changing the grid does not guess how to refit imported art. Adjust placements and visual-child transforms as appropriate for the new metric.

Export subtracts the origin and divides by cell size before flooring a box center to its integer cell. Floor surfaces lie on level planes, while volume centers are half a level above them. The exported map persists the metrics for picking, unit positions/size, playback, path overlays, highlights and camera framing. Gameplay ranges still count integer cells.

## Export and verification

Set `TargetPath`, then click **Export BattleMap** on the root. The gameplay baker receives only footprint resources and transforms. Scene export separately preserves visual nodes, nested scenes and overrides. It adds collision-layer-1 picking surfaces only for explicit floors/top faces; authored model colliders are copied onto non-ground layers so an unmarked roof cannot become mouse-pickable ground. Source nodes/resources are unchanged.

The exporter rejects invalid dimensions/transforms, misaligned cells, negative coordinates, duplicate floors, solid overlaps and blocked spawns. Nonblocking contributions may overlap. Missing floors are nonwalkable but do not automatically block sight. Bounds include volume-only cells and roof floors; nonzero starting coordinates are preserved.

Save and reload the authoring scene, export, reload the baked scene and re-export after modifying an asset. The example output is `resources/maps/prop_example_map.tscn`.

Existing movement rules are unchanged: adjacent walkable, unoccupied cells directly above/below one another already connect. A solid van cell blocks the direct step through its interior, but declaring a roof surface does not itself implement ladders, climbing or a new clearance model. Those features and rectangle authoring are deferred.
