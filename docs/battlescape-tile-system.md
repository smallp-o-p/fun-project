# Battlescape Tile System Design

The tactical board is tile-authoritative. Gameplay truth lives in board state, not in meshes or scene nodes. Presentation becomes richer without changing movement, line of sight, or occupancy.

Runtime ownership: [battlescape-architecture.md](battlescape-architecture.md). Map authoring: [battle-map-authoring.md](battle-map-authoring.md).

## Core Decision

The project is tile-first:

- the board is the authoritative gameplay model;
- environment visuals are generated from or attached to tiles;
- large props declare a tile footprint instead of becoming a second source of truth.

The project is not environment-first with a gameplay grid projected onto arbitrary geometry.

## Ownership Model

### Authoritative runtime

`BattleSession`, `BattleBoardState`, `BattleTileState`, and `BattleUnitState` own:

- which cells exist and which are in bounds
- walkability, LOS blocking, and cover per tile
- unit and object occupancy, placement, movement, and removal
- board-local pathfinding

`BattleTileState` carries `IsWalkable`, `BlocksLineOfSight`, `BlocksVerticalLineOfSight`, and `Cover` (direction + amount). These fields are independent; cover is never inferred. Authored `BattleMapTileData` mirrors the same fields per map cell.

### Authored map data

`BattleMapData` is the authored map: `Dimensions` plus a `Tiles` dictionary keyed by `Vector3I`. It is an authoring input, not runtime truth. `new BattleBoardState(mapData)` derives the board: all cells start unwalkable, then authored tiles apply. Unpainted cells are holes. Detail: [battle-map-authoring.md](battle-map-authoring.md).

### Presentation

Scene nodes own floor and prop meshes, tile highlights, hover and selection feedback, animations, and VFX. They read board and map data. They never decide gameplay truth.

### Input

Input resolves the cursor to a cell coordinate and hands that `Vector3I` to controller code. It owns no tactical meaning beyond identifying the targeted cell.

## Coordinate Conventions

Board coordinates are the engine-agnostic `FunProject.Core.Vector3I` (X = width, Y = levels, Z = depth). Authored resources use Godot's `Vector3I`; the runtime boundary converts explicitly. One tile is one cell; cell centers sit at `(x + 0.5, y + 0.5, z + 0.5)` in board-local space. Keep one shared set of world-to-cell and cell-to-world rules; do not duplicate them across selection, LOS, movement preview, and rendering.

## Adjacency and Pathfinding

Adjacency is eight-way: orthogonal plus same-level diagonals. A diagonal step needs at least one open flank, so sealed corners cannot be cut. Pathfinding lives in `BattleBoardState` as a pure-C# breadth-first search with a fixed direction order, so tie-breaks are deterministic. It is exposed as `FindPath`.

Battle legality — whether a unit may act, AP spending, turn checks — stays with the session and actions. Controllers preview routes through `FindPathForUnit` and `GetPossibleMoveTilesForUnit`, then submit destination steps to `MoveUnit`. That action validates the whole route once (total AP, adjacency, occupancy), caches it, and re-checks only liveness, incapacity, and next-cell occupancy on each later step. If pathfinding later becomes heavily unit-specific, it can move behind a movement-query strategy without changing that contract.

## Picking

Tile picking is board-level raycast-to-cell, not one `Area3D` per tile. Per-tile collision nodes scale poorly: more nodes, more synchronization between visuals and cells. `BattleInputController` reads the mouse, builds a ray from the `GameCamera` rig, and converts the hit to a cell for hover, selection, and action targeting.

The long-term model for complex environments stays raycast-based: intersect floor and prop colliders, or traverse the grid along the ray, then convert the hit to a cell.

## Relationship to `Interactable`

`Interactable`/`IInteractable` remain useful for world-space buttons and props that need direct hover or click behavior. They must not become the representation of tactical tiles: a tile is a board cell, not a scene node. A presentation helper may render a tile, but it never owns walkability, LOS, occupancy, or cover state.

## Non-Goals

The tile system does not infer gameplay state from environment meshes, does not make decorative meshes authoritative for movement or LOS, and does not duplicate coordinate conversion across systems.

Ramps, stacked floors, and multi-tile props are future work. When they arrive, they extend the authored tile data and this board model; they do not move gameplay truth into scene nodes.
