# Battlescape Tile System Design

This document defines how tactical tiles should work in the battlescape.

It describes the intended split between authoritative gameplay state, static map data, presentation, and player input. It also defines the first proof-of-concept scope: flat ground only.

## Goals

- Keep the tactical board tile-authoritative.
- Avoid splitting gameplay truth across meshes, scene hierarchy, and runtime state.
- Let presentation become richer over time without changing how movement, line of sight, and occupancy work.
- Support a simple flat-ground proof of concept first.
- Preserve a clean path to ramps, stacked floors, walls, cover, hazards, and destructible props later.

## Core Decision

The project should be tile-first.

That means:

- the board is the authoritative gameplay model
- environment visuals are generated from or attached to tiles
- larger environment props declare a tile footprint instead of becoming a separate source of tactical truth

The project should not be environment-first with a separate gameplay grid projected onto arbitrary geometry.

## Why Tile-First Fits This Repo

The current runtime already treats the battlescape as a 3D tile board:

- `BattleBoardState` owns a `BattleTileState` for every `Vector3I` coordinate
- `BattleTileState` already owns walkability, line-of-sight blocking, hazard state, and occupancy
- `BattleVisibilitySystem` already performs tile-based line-of-sight queries from tile center to tile center

This means the tile system is not a new abstraction. It is an extension of the runtime model that already exists.

## Ownership Model

### Authoritative Runtime

The authoritative tactical state stays in runtime classes such as:

- `BattleSession`
- `BattleBoardState`
- `BattleTileState`
- `BattleUnitState`

These classes own:

- which cells exist
- whether a cell is in bounds
- whether a cell is walkable
- whether a cell blocks line of sight
- which unit occupies a cell
- hazard state and future tactical modifiers

### Static Map Data

Static map definitions should eventually live in Godot `Resource` classes.

They should describe:

- board dimensions
- base floor type per cell
- elevation per cell or per layer
- whether a cell is present or absent
- optional prop placement metadata
- optional tactical overrides such as walkability or LOS blocking

These resources are authoring inputs, not runtime truth.

### Presentation

Scene nodes own:

- floor meshes
- prop meshes
- tile highlights
- hover feedback
- selection feedback
- animations and VFX

Presentation reads board and map data. It does not decide gameplay truth.

### Input

Input should resolve the mouse or cursor to a cell coordinate, then hand that `Vector3I` to controller code.

The input layer should not own tactical meaning beyond identifying the targeted cell.

## Recommended Structure

The tile system should evolve toward these responsibilities:

- `BattleMapData`
  - static resource describing a map layout
- `BattleMapTileData`
  - static per-cell authoring data
- `BattleBoardState`
  - authoritative runtime tile state derived from setup and mutations
- `BattleBoardPresenter`
  - scene-side builder that instantiates visuals from map or board data
- `BattleSceneController`
  - translates selected or hovered cells into actions

## Coordinate Conventions

Board coordinates should continue using `Godot.Vector3I` semantics:

- `X` = width
- `Y` = levels / height
- `Z` = length / depth

For the initial proof of concept:

- one tile is one gameplay cell
- tile size is `1.0`
- the playable surface is flat ground at `Y = 0`
- board cell centers are at `(x + 0.5, y + 0.5, z + 0.5)` in board-local space

The project should keep one shared set of world-to-cell and cell-to-world conversion rules. Those rules should not be duplicated across selection, LOS, movement preview, and rendering code.

## Selection and Hover Model

The recommended long-term picking model is board-level raycast-to-cell, not one `Area3D` per tile.

### Why

Per-tile `Area3D` nodes are simple for a prototype, but they scale poorly as the board grows:

- more scene nodes
- more collision objects
- more per-tile interaction plumbing
- more synchronization work between visual tiles and runtime cells

Board-level picking keeps the gameplay target as a cell coordinate from the start.

### Flat-Ground POC

For flat ground, tile picking should work like this:

1. Read mouse position from the viewport.
2. Use the active `Camera3D` to produce a world-space ray.
3. Intersect that ray with the flat ground plane.
4. Convert the hit point into board-local space.
5. Convert the board-local hit point into a `Vector3I` cell.
6. Reject the result if the cell is out of bounds.
7. Use that cell for hover highlight, selection, or action targeting.

This allows a proof of concept without creating a separate interactable node for every tile.

### Future Picking

When the environment becomes more complex, the system can move to either:

- physics raycasts against floor and prop colliders, then convert hit position to a cell
- full grid traversal along the mouse ray, similar to voxel DDA, to determine the first targeted cell or face

The first option is simpler. The second option becomes more attractive once the board has vertical stacks, bridges, overhangs, or layered targeting rules.

## Relationship to `Interactable`

`Interactable` and `IInteractable` can still be useful for scene objects such as:

- buttons in the 3D world
- props that need direct hover or click behavior
- quick prototypes of tile selection

They should not become the primary representation of tactical tiles.

The tactical tile should remain a board cell, not a scene node.

If a temporary `BattleTileView : Interactable` prototype is used, it should be treated as a presentation helper only. It should not own walkability, LOS blocking, occupancy, hazards, or cover state.

## Flat-Ground Proof of Concept Scope

The first tile proof of concept should intentionally stay small.

### Included

- rectangular board dimensions
- flat floor at `Y = 0`
- simple generated floor visuals
- board-level cell picking
- hover highlight for the current cell
- selection of a clicked cell
- conversion between cell coordinates and world positions

### Excluded

- ramps
- multiple vertical floors
- partial cover
- doors
- destructible props
- non-rectangular maps
- tile materials with tactical meaning beyond simple walkability and LOS blocking

## Recommended Implementation Order

1. Add a dedicated tile-system document and keep the vocabulary aligned with the battlescape architecture.
2. Add shared grid math helpers for:
   - `CellToWorldCenter`
   - `WorldToCell`
3. Add a minimal `BattleMapData` resource for flat ground maps.
4. Add a `BattleBoardPresenter` scene node that renders a simple flat board.
5. Add board-level raycast-to-cell input.
6. Add hover and selected-cell presentation.
7. Only after that, add richer tile metadata and environment props.

## Future Extensions

Once the flat-ground slice works, the next extensions should be:

- cell materials or themes
- per-cell tactical metadata
- multi-tile props with declared footprints
- raised tiles and stacked floors
- ramps and climb transitions
- cover evaluation
- destructible environment pieces that stamp changes back into board state
- fog-of-war overlays driven from faction visibility

## Non-Goals

The tile system should not:

- infer gameplay state from arbitrary environment meshes
- make decorative meshes authoritative for movement or LOS
- rely on one permanent scene node per gameplay tile as the only supported interaction model
- duplicate coordinate conversion rules in multiple systems

## Summary

The tactical tile is a board cell, not a mesh.

Map data describes tiles.
Runtime state owns tiles.
Presentation renders tiles.
Input resolves to tiles.

That separation fits the current runtime, keeps the first proof of concept small, and leaves room for richer environment art later without rewriting the tactical foundation.
