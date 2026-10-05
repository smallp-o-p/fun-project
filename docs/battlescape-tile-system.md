# Battlescape Tile System Design

The tactical board owns gameplay state. Scene nodes display the board and provide input, animation, and visual effects.

Runtime ownership: [battlescape-architecture.md](battlescape-architecture.md). Map authoring: [battle-map-authoring.md](battle-map-authoring.md).

## Ownership

`BattleSession`, `BattleBoardState`, `BattleTileState`, and `BattleUnitState` own:

- cell bounds and walkability
- line-of-sight blocking and cover
- unit and object occupancy, placement, movement, and removal
- board-local pathfinding

`BattleTileState` carries `IsWalkable`, `BlocksLineOfSight`, `BlocksVerticalLineOfSight`, and `Cover` (direction and amount). Cover comes from explicit authored footprint flags. `BattleMapTileData` stores the baked fields for each cell.

## Map data

`BattleMapData` stores `Dimensions`, three flattened byte arrays (walkability/LOS flags, cover directions and cover amounts), and sparse `SpawnSlots`. Cell access uses `GetTile`/`SetTile`; `GetTile` materializes a `BattleMapTileData` Resource owned by the caller. Both baked and runtime cells use `x + width * (z + depth * y)`. The map baker expands entity footprints into cell flags. A ground cell is walkable when it has a floor and is free of movement blockers.

`new BattleBoardState(mapData)` creates an independent flat array of materialized tile structs. Runtime `GetTile` returns a reference to the cell; use a `ref` local to retain mutable access, since ordinary assignment copies its value. Each cell starts unwalkable, then receives its baked tile data. Unpainted cells remain unwalkable holes. See [battle-map-authoring.md](battle-map-authoring.md).

## Presentation and input

Scene nodes own floor and prop meshes, tile highlights, hover and selection feedback, animations, and VFX. They read board and map data through the runtime boundary.

Input resolves the cursor to a cell coordinate and passes that `Vector3I` to controller code. Controllers use runtime queries and actions to interpret the cell.

`Interactable` and `IInteractable` support world-space buttons and props with direct hover or click behavior. Tile presentation uses board cells for walkability, LOS, occupancy, and cover.

## Coordinates

Runtime coordinates use `FunProject.Core.Vector3I`; authored resources use Godot's `Vector3I`. The runtime boundary converts them explicitly.

- X = width
- Y = levels
- Z = depth

One tile is one cell. Cell centers sit at `(x + 0.5, y + 0.5, z + 0.5)` in board-local space. Use shared world-to-cell and cell-to-world conversions for selection, LOS, movement preview, and rendering.

## Adjacency and pathfinding

Adjacency includes orthogonal and same-level diagonal steps. A diagonal needs at least one open flank. `BattleBoardState.FindPath` uses breadth-first search with a fixed direction order for deterministic tie-breaks.

The session and actions check battle legality, spend AP, and apply turn rules. Controllers preview routes through `FindPathForUnit` and `GetPossibleMoveTilesForUnit`, then submit destination steps to `MoveUnit`.

`MoveUnit` validates total AP, adjacency, and occupancy once for the complete route. It caches the route. Each later step re-checks liveness, incapacity, and next-cell occupancy.

## Picking

`BattleInputController` builds a ray from the `GameCamera` rig and converts the hit to a board cell. That cell drives hover, selection, and action targeting.

## Supported geometry

Map authoring supports multi-tile prop footprints, including upper volumes for movement and sight blocking. Each volume needs ground floor support. Stacked walkable floors and ramps remain deferred.
