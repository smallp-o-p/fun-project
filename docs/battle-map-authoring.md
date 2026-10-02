# Battle map authoring

Open `scenes/battle/authoring/BattleMapAuthoring.tscn` in Godot 4.7.2 .NET. Paint terrain using the GridMap and its `BattleTilePalette`. Palette brushes describe independent movement, line-of-sight, vertical sight blocking, four directional cover strengths and optional spawn slots.

For a ready-to-export example, open `scenes/battle/authoring/PropExample.tscn`. Its output is `resources/maps/prop_example_map.tscn`.

## Reusable props

Instance `scenes/battle/props/ExampleCar.tscn` as a **direct child** of the authoring GridMap. Its root is `BattlePropAuthoring`; its children are the visual model. Drag and rotate with the standard editor gizmo, then press **Snap to grid** to align the root pivot to the nearest supporting ground and a 90-degree Y rotation. The scene transform is the placement; there is no separate anchor/rotation state. Press **Refresh footprint preview** after editing footprint or cover values.

The prop scene root holds a flat set of local footprint cells, separate movement/LOS flags, and four outward cover strengths (North/East/South/West, 0–100). Each strength applies along that side’s exposed footprint boundaries; internal boundaries are skipped. The origin must be in the footprint. Reuse the scene itself; no separate prop definition or edge resources are needed. The example occupies `(0,0,0)` and `(0,0,1)`; it blocks walking, allows sight and provides 40 cover around its six exterior edges. This is a simple placeholder, not physical projectile collision.

The model's root pivot contacts the ground at the anchor cell center. Offset visual children once to fit the footprint. Terrain brushes supply `GroundSurfaceOffset` relative to their integer level; the debug floor's 0.1-high centered box uses **0.05**. Every supporting tile must be walkable, at the same level and have the same surface offset. Props do not create new terrain.

Footprint outlines and outward cover marks are editor-only. Placement and data are checked at export, not continuously while editing. Only axis-aligned, unscaled, untilted placements are supported. The GridMap uses one-unit cells, X/Z centered, Y uncentered, and an identity transform. Nonblocking decorations may overlap; two movement-blocking props may not.

## Export

Set **Target Path** to a `.tscn` or `.scn`, then click **Export BattleMap**. Export validates painted cells, metadata, transforms, support, solid overlaps and blocked spawns before saving anything. It creates a `BattleMap` scene with fresh per-cell gameplay data, the painted GridMap and owned prop visuals. Palette resources remain unchanged. Open the exported scene to inspect it, then use that PackedScene in the battle type's map pool.

Coordinates are preserved, including maps starting away from zero. Dimensions are maximum coordinates plus one; missing cells remain holes. Negative coordinates are unsupported.

Cover belongs to the unit's **standing tile**, facing the obstacle. A car east of a unit stamps East cover into that unit's tile. Rotating the car rotates its footprint and four cover sides. Only existing, ultimately walkable neighbors receive cover. Contributions merge by maximum **per side**, so North 20 and East 60 stay distinct. A diagonal shot uses the strongest of its two approach sides (the existing either-component rule); amounts do not add. Height differences retain the existing horizontal cover rule.

Terrain collision is layer 1 for mouse ground picking. Exported prop collisions use layer 2 so a roof cannot select a false ground level. Logical movement and visibility use baked tile data, independently of physics geometry. Authoring metadata and previews are not shipped as runtime prop systems.

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
