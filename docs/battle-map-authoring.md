# Battle map authoring

Open `scenes/battle/authoring/BattleMapAuthoring.tscn` in Godot 4.7.2 .NET. Paint terrain on the root GridMap and props on its **Props** child GridMap. Both layers use the same `MeshLibrary` and existing `BattleTilePalette`; setting the palette library automatically adds a brush for each named mesh item.

For a ready-to-export example, open `scenes/battle/authoring/PropExample.tscn`. Its output is `resources/maps/prop_example_map.tscn`.

## Reusable props

Add a static prop mesh to the shared MeshLibrary, then set its palette brush's `PropFootprint`, `PropBlocksMovement`, `BlocksLineOfSight` and `PropCoverNorth/East/South/West`. Select **Props**, choose that mesh in the GridMap palette and paint one anchor cell per prop. Use the GridMap's rotation controls for upright 90-degree turns around Y. The painted cell and its orientation are the only placement state; no separate prop scenes or snapping tools are required.

`PropFootprint` is a flat set of local cells including `(0,0,0)`. The four **Prop Cover** strengths (0–100) describe outward cover along exposed footprint boundaries; internal boundaries are skipped. They are separate from **Cover North/East/South/West**, which describe cover received by a unit standing on a terrain tile. `BlocksLineOfSight` is shared brush metadata; prop movement blocking is independently controlled by `PropBlocksMovement`.

`PropExampleMeshLibrary.tres` preserves the debug terrain items and adds **ExampleCar**, one merged mesh with five material surfaces and box collisions. Its footprint is `(0,0,0)` plus `(0,0,1)`; it blocks walking, allows sight and provides **40** cover around all six exterior edges. `PropExample.tscn` paints it at `(2,0,2)` on the Props layer.

Author each mesh around a ground-contact pivot at the anchor cell center. Both GridMaps use one-unit cells, X/Z centered and Y uncentered; the terrain root is identity-transformed. Props is a direct child bound to the root's **Props** property, with no X/Z offset, rotation, scaling or cell scaling. Its Y offset must match `GroundSurfaceOffset` on every supporting terrain tile. The debug floor's centered 0.1-high box uses **0.05**, so the example Props layer sits at Y **0.05**. All supporting cells must be walkable, at the anchor's integer level and have the same surface offset. A single Props layer therefore supports one shared ground-surface offset; props do not create terrain.

Export checks placement and metadata. Tilted/upside-down cell orientations and overlapping movement-blocking footprints are rejected. Nonblocking decorations may overlap footprints.

## Export

Set **Target Path** to a `.tscn` or `.scn`, then click **Export BattleMap**. Export validates painted cells, metadata, transforms, support, solid overlaps and blocked spawns before saving anything. It creates a `BattleMap` scene with fresh per-cell gameplay data, the terrain GridMap and a plain child Props GridMap retaining its library, cells and orientations. Palette resources remain unchanged. Open the exported scene to inspect it, then use that PackedScene in the battle type's map pool.

Coordinates are preserved, including maps starting away from zero. Dimensions are maximum coordinates plus one; missing cells remain holes. Negative coordinates are unsupported.

Cover belongs to the unit's **standing tile**, facing the obstacle. A car east of a unit stamps East cover into that unit's tile. Rotating the car rotates its footprint and four cover sides. Only existing, ultimately walkable neighbors receive cover. Contributions merge by maximum **per side**, so North 20 and East 60 stay distinct. A diagonal shot uses the strongest of its two approach sides (the existing either-component rule); amounts do not add. Height differences retain the existing horizontal cover rule.

Terrain collision is layer 1 for mouse ground picking. Props collision is layer 2 so a roof cannot select a false ground level. Logical movement, sight and cover use baked tile data, independently of MeshLibrary collision shapes. These props are static meshes, not runtime entities: they cannot be targeted, damaged, destroyed or moved independently in battle.

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
