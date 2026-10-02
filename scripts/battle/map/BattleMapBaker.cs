using System;
using System.Collections.Generic;
using Cell = Godot.Vector3I;

namespace FunProject.Battle;

public static class BattleMapBaker
{
  public static BattleMapData Bake(
    Godot.Collections.Dictionary<Cell, BattleMapTileData> cells,
    IReadOnlyList<BattlePropPlacement> props)
  {
    if (cells is null || props is null)
      throw new InvalidOperationException("Map baking requires ground cells and prop placements.");

    var baked = new BattleMapData();
    Cell dimensions = Cell.Zero;
    foreach (var (cell, tile) in cells)
    {
      ValidateCoordinates(cell, $"Ground cell {cell}");
      if (tile is null)
        throw new InvalidOperationException($"Ground cell {cell} has no tile data.");
      if (!float.IsFinite(tile.GroundSurfaceOffset))
        throw new InvalidOperationException($"Ground cell {cell} has a non-finite surface offset.");
      dimensions = new Cell(Math.Max(dimensions.X, cell.X + 1),
        Math.Max(dimensions.Y, cell.Y + 1), Math.Max(dimensions.Z, cell.Z + 1));
      baked.Tiles[cell] = Clone(tile);
    }
    baked.Dimensions = dimensions;

    var solidCells = new SysColGeneric.HashSet<Cell>();
    for (int i = 0; i < props.Count; i++)
    {
      BattlePropPlacement placement = props[i];
      string context = $"Prop {i} at {placement.Anchor}";
      ValidateDefinition(placement.Definition, context);
      ValidateCoordinates(placement.Anchor, context);
      float? surface = null;
      foreach (Cell localCell in placement.Definition.Footprint)
      {
        Cell cell = placement.Anchor + Rotate(localCell, placement.QuarterTurns);
        ValidateCoordinates(cell, context);
        // Support is checked against the original ground, never against earlier prop stamps.
        if (!cells.TryGetValue(cell, out BattleMapTileData? ground) || !ground.Walkable)
          throw new InvalidOperationException($"{context} requires existing walkable ground at {cell}.");
        if (surface.HasValue && surface.Value != ground.GroundSurfaceOffset)
          throw new InvalidOperationException($"{context} spans unequal ground surface offsets at {cell}.");
        surface = ground.GroundSurfaceOffset;

        BattleMapTileData tile = baked.Tiles[cell];
        if (placement.Definition.BlocksMovement)
        {
          if (!solidCells.Add(cell))
            throw new InvalidOperationException($"{context} overlaps another movement-blocking prop at {cell}.");
          tile.Walkable = false;
        }
        tile.BlocksLineOfSight |= placement.Definition.BlocksLineOfSight;
      }
    }

    // Apply edges after every footprint so a later solid prop cannot become a standing cell.
    foreach (BattlePropPlacement placement in props)
    {
      foreach (BattlePropCoverEdgeData edge in placement.Definition.CoverEdges)
      {
        CoverDirections outward = Rotate(edge.Direction, placement.QuarterTurns);
        Cell standingCell = placement.Anchor + Rotate(edge.Cell, placement.QuarterTurns) + DirectionOffset(outward);
        if (baked.Tiles.TryGetValue(standingCell, out BattleMapTileData? tile) && tile.Walkable)
          MergeCover(tile, Rotate(outward, 2), edge.Amount);
      }
    }

    foreach (var (cell, tile) in baked.Tiles)
    {
      if (tile.SpawnFactionSlot >= 0 && !tile.Walkable)
        throw new InvalidOperationException($"Spawn slot {tile.SpawnFactionSlot} at {cell} is blocked for movement.");
    }
    return baked;
  }

  // Godot's positive Y rotation sends +X toward -Z.
  public static Cell Rotate(Cell cell, int quarterTurns) => NormalizeTurns(quarterTurns) switch
  {
    1 => new Cell(cell.Z, cell.Y, -cell.X),
    2 => new Cell(-cell.X, cell.Y, -cell.Z),
    3 => new Cell(-cell.Z, cell.Y, cell.X),
    _ => cell,
  };

  public static CoverDirections Rotate(CoverDirections direction, int quarterTurns)
  {
    Cell rotated = Rotate(DirectionOffset(direction), quarterTurns);
    if (rotated.X == 1)
      return CoverDirections.East;
    if (rotated.X == -1)
      return CoverDirections.West;
    return rotated.Z == -1 ? CoverDirections.North : CoverDirections.South;
  }

  private static int NormalizeTurns(int quarterTurns) => (quarterTurns % 4 + 4) % 4;

  private static void ValidateCoordinates(Cell cell, string context)
  {
    if (cell.X < 0 || cell.Y < 0 || cell.Z < 0)
      throw new InvalidOperationException($"{context} has a negative board coordinate {cell}.");
    if (cell.X == int.MaxValue || cell.Y == int.MaxValue || cell.Z == int.MaxValue)
      throw new InvalidOperationException($"{context} exceeds the supported board dimensions at {cell}.");
  }

  private static void ValidateDefinition(BattlePropData definition, string context)
  {
    if (definition is null)
      throw new InvalidOperationException($"{context} has no prop definition.");
    if (definition.Footprint is null || definition.Footprint.Count == 0)
      throw new InvalidOperationException($"{context} requires a nonempty footprint.");

    var footprint = new SysColGeneric.HashSet<Cell>();
    foreach (Cell cell in definition.Footprint)
    {
      if (cell.Y != 0)
        throw new InvalidOperationException($"{context} footprint cell {cell} must have local Y = 0.");
      if (!footprint.Add(cell))
        throw new InvalidOperationException($"{context} contains duplicate footprint cell {cell}.");
    }
    if (!footprint.Contains(Cell.Zero))
      throw new InvalidOperationException($"{context} footprint must contain its anchor cell (0, 0, 0).");
    if (definition.CoverEdges is null)
      throw new InvalidOperationException($"{context} has no cover edge collection.");

    foreach (BattlePropCoverEdgeData edge in definition.CoverEdges)
    {
      if (edge is null)
        throw new InvalidOperationException($"{context} contains an empty cover edge.");
      if (!footprint.Contains(edge.Cell))
        throw new InvalidOperationException($"{context} cover edge cell {edge.Cell} is outside its footprint.");
      if (edge.Direction is not (CoverDirections.North or CoverDirections.East or CoverDirections.South or CoverDirections.West))
        throw new InvalidOperationException($"{context} cover edge at {edge.Cell} must have exactly one cardinal direction.");
      if (footprint.Contains(edge.Cell + DirectionOffset(edge.Direction)))
        throw new InvalidOperationException($"{context} cover edge at {edge.Cell} faces an internal footprint cell.");
      if (edge.Amount < 0 || edge.Amount > 100)
        throw new InvalidOperationException($"{context} cover edge at {edge.Cell} amount must be between 0 and 100.");
    }
  }

  private static Cell DirectionOffset(CoverDirections direction) => direction switch
  {
    CoverDirections.North => new Cell(0, 0, -1),
    CoverDirections.East => new Cell(1, 0, 0),
    CoverDirections.South => new Cell(0, 0, 1),
    CoverDirections.West => new Cell(-1, 0, 0),
    _ => throw new InvalidOperationException($"Cover direction {direction} must be exactly one cardinal heading."),
  };

  private static void MergeCover(BattleMapTileData tile, CoverDirections direction, int amount)
  {
    switch (direction)
    {
      case CoverDirections.North:
        tile.CoverNorth = Math.Max(tile.CoverNorth, amount);
        break;
      case CoverDirections.East:
        tile.CoverEast = Math.Max(tile.CoverEast, amount);
        break;
      case CoverDirections.South:
        tile.CoverSouth = Math.Max(tile.CoverSouth, amount);
        break;
      case CoverDirections.West:
        tile.CoverWest = Math.Max(tile.CoverWest, amount);
        break;
    }
  }

  private static BattleMapTileData Clone(BattleMapTileData tile) => new()
  {
    Walkable = tile.Walkable,
    BlocksLineOfSight = tile.BlocksLineOfSight,
    BlocksVerticalLineOfSight = tile.BlocksVerticalLineOfSight,
    GroundSurfaceOffset = tile.GroundSurfaceOffset,
    CoverNorth = tile.CoverNorth,
    CoverEast = tile.CoverEast,
    CoverSouth = tile.CoverSouth,
    CoverWest = tile.CoverWest,
    SpawnFactionSlot = tile.SpawnFactionSlot,
  };
}
