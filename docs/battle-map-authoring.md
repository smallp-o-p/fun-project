# Battle map authoring

Open `scenes/battle/authoring/BattleMapAuthoring.tscn` in Godot 4.7.2 .NET. Paint terrain using the GridMap and its `BattleTilePalette`. Palette brushes describe independent movement, line-of-sight, vertical sight blocking, four directional cover strengths and optional spawn slots.

For a ready-to-export example, open `scenes/battle/authoring/PropExample.tscn`. Its output is `resources/maps/prop_example_map.tscn`.

## Reusable props

Instance `scenes/battle/props/ExampleCar.tscn` as a **direct child** of the authoring GridMap. Its root is `BattlePropAuthoring`; its children are the visual model. Set **Anchor** (the first occupied tile) and **Quarter Turns** (0–3, positive Godot Y rotation). These controls position the prop on the ground surface. After dragging or rotating with the standard editor gizmo, press **Snap to grid**. Press **Refresh preview** after editing shared metadata or terrain.

A prop definition contains a flat set of local footprint cells, separate movement/LOS flags, and explicit exterior cover edges. An edge identifies its local occupied cell, one outward cardinal direction and strength (0–100). The origin must be in the footprint. Interior edges are invalid. The example occupies `(0,0,0)` and `(0,0,1)`; it blocks walking, allows sight and provides 40 cover around its six exterior edges. This is a simple placeholder, not physical projectile collision.

The model's root pivot contacts the ground at the anchor cell center. Offset visual children once to fit the footprint. Terrain brushes supply `GroundSurfaceOffset` relative to their integer level; the debug floor's 0.1-high centered box uses **0.05**. Every supporting tile must be walkable, at the same level and have the same surface offset. Props do not create new terrain.

Footprint outlines and cover arrows are editor-only. Invalid placements are red. Only axis-aligned, unscaled, untilted placements are supported. The GridMap uses one-unit cells, X/Z centered, Y uncentered, and an identity transform. Nonblocking decorations may overlap; two movement-blocking props may not.

## Export

Set **Target Path** to a `.tscn` or `.scn`, then click **Export BattleMap**. Export validates painted cells, metadata, transforms, support, solid overlaps and blocked spawns before saving anything. It creates a `BattleMap` scene with fresh per-cell gameplay data, the painted GridMap and owned prop visuals. Palette resources remain unchanged. Open the exported scene to inspect it, then use that PackedScene in the battle type's map pool.

Coordinates are preserved, including maps starting away from zero. Dimensions are maximum coordinates plus one; missing cells remain holes. Negative coordinates are unsupported.

Cover belongs to the unit's **standing tile**, facing the obstacle. A car east of a unit stamps East cover into that unit's tile. Rotating the car rotates both footprint and edges. Only existing, ultimately walkable neighbors receive cover. Contributions merge by maximum **per side**, so North 20 and East 60 stay distinct. A diagonal shot uses the strongest of its two approach sides (the existing either-component rule); amounts do not add. Height differences retain the existing horizontal cover rule.

Terrain collision is layer 1 for mouse ground picking. Exported prop collisions use layer 2 so a roof cannot select a false ground level. Logical movement and visibility use baked tile data, independently of physics geometry. Authoring metadata and previews are not shipped as runtime prop systems.
