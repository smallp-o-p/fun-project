using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FunProject.Battle;

public sealed class BattleBoardState
{
  public readonly struct ValidatedPoint : IEquatable<ValidatedPoint>, IEquatable<Vector3I>
  {
    public readonly Vector3I Raw;
    public int X => Raw.X;
    public int Y => Raw.Y;
    public int Z => Raw.Z;

    internal ValidatedPoint(Vector3I coordinates)
    {
      Raw = coordinates;
    }

    public bool Equals(ValidatedPoint other)
    {
      return Raw == other.Raw;
    }

    public bool Equals(Vector3I other)
    {
      return Raw == other;
    }

    public override bool Equals(object? obj)
    {
      return (obj is ValidatedPoint other && Equals(other)) || (obj is Vector3I other2 && Equals(other2));
    }

    public override int GetHashCode()
    {
      return Raw.GetHashCode();
    }

    public override string ToString()
    {
      return Raw.ToString();
    }

    public static bool operator ==(ValidatedPoint left, ValidatedPoint right)
    {
      return left.Equals(right);
    }

    public static bool operator !=(ValidatedPoint left, ValidatedPoint right)
    {
      return !(left == right);
    }
  }

  private readonly BattleTileState[,,] _tiles;
  private readonly AStar3D _pathGraph = new();

  public Vector3I Dimensions { get; }

  public BattleBoardState(Vector3I dim)
  {
    if (dim.X <= 0 || dim.Y <= 0 || dim.Z <= 0)
      throw new ArgumentOutOfRangeException(nameof(dim));

    Dimensions = dim;
    _tiles = new BattleTileState[Dimensions.X, Dimensions.Y, Dimensions.Z];

    for (int y = 0; y < Dimensions.Y; y++)
    {
      for (int z = 0; z < Dimensions.Z; z++)
      {
        for (int x = 0; x < Dimensions.X; x++)
        {
          ValidatedPoint point = new(new Vector3I(x, y, z));
          BattleTileState tile = new();
          tile.TraversalStateChanged += changedTile => OnTileTraversalStateChanged(point, changedTile);
          _tiles[x, y, z] = tile;
        }
      }
    }

    InitializePathGraph();
  }

  private bool IsInBounds(Vector3I coordinates)
  {
    return coordinates.X >= 0
      && coordinates.Y >= 0
      && coordinates.Z >= 0
      && coordinates.X < Dimensions.X
      && coordinates.Y < Dimensions.Y
      && coordinates.Z < Dimensions.Z;
  }

  public Option<ValidatedPoint> ValidatePoint(Vector3I coordinates)
  {
    return IsInBounds(coordinates) ? Some(new ValidatedPoint(coordinates)) : None;
  }

  public Option<ValidatedPoint> ValidatePoint(Godot.Vector3 coordinates)
  {
    Godot.Vector3 floor = coordinates.Floor();
    return ValidatePoint(new((int)floor.X, (int)floor.Y, (int)floor.Z));
  }

  public Option<List<ValidatedPoint>> ValidatePath(IEnumerable<Vector3I> path)
  {
    List<ValidatedPoint> validatedPts = new()
    {
      Capacity = path.Count()
    };

    foreach (var pt in path)
    {
      bool validated = ValidatePoint(pt).Match(
        (validated) =>
        {
          validatedPts.Add(validated);
          return true;
        },
        () => false
      );

      if (!validated)
        return None;
    }

    return Some(validatedPts);
  }

  public bool TryPlaceOccupant(ValidatedPoint point, int unitId)
  {
    return GetTile(point).TrySetOccupant(unitId);
  }

  public bool TryMoveOccupant(ValidatedPoint source, ValidatedPoint destination, int unitId)
  {
    BattleTileState sourceTile = GetTile(source), destinationTile = GetTile(destination);

    if (!sourceTile.HasOccupant(unitId))
      return false;
    if (source.Equals(destination))
      return true;
    if (!destinationTile.TrySetOccupant(unitId))
      return false;

    sourceTile.ClearOccupant();
    return true;
  }

  public bool TryClearOccupant(ValidatedPoint point, int unitId)
  {
    BattleTileState tile = GetTile(point);
    if (!tile.HasOccupant(unitId))
      return false;

    tile.ClearOccupant();
    return true;
  }

  public ValidatedPoint[] FindPath(ValidatedPoint source, ValidatedPoint destination, int movingUnitId = 0)
  {
    if (!CanUsePathEndpoint(source, movingUnitId) || !CanUsePathEndpoint(destination, movingUnitId))
      return [];

    long sourceId = CoordinatesToPointId(source);
    long destinationId = CoordinatesToPointId(destination);
    BattleTileState sourceTile = GetTile(source);
    bool restoreSourceDisabled = false;

    if (sourceTile.HasOccupant(movingUnitId) && _pathGraph.IsPointDisabled(sourceId))
    {
      _pathGraph.SetPointDisabled(sourceId, false);
      restoreSourceDisabled = true;
    }

    ValidatedPoint[] path = System.Array.ConvertAll(_pathGraph.GetIdPath(sourceId, destinationId), PointIdToCoordinates);
    if (restoreSourceDisabled)
      _pathGraph.SetPointDisabled(sourceId, ShouldDisablePathPoint(sourceTile));

    return path;
  }

  public bool CanOccupy(ValidatedPoint point)
  {
    BattleTileState tile = GetTile(point);
    return tile.IsWalkable && !tile.IsOccupied;
  }

  public static bool AreAdjacent(ValidatedPoint source, ValidatedPoint destination)
  {
    Vector3I delta = source.Raw - destination.Raw;
    return Mathf.Abs(delta.X) + Mathf.Abs(delta.Y) + Mathf.Abs(delta.Z) == 1;
  }

  public IEnumerable<ValidatedPoint> EnumerateBoardPoints()
  {
    for (int y = 0; y < Dimensions.Y; y++)
    {
      for (int z = 0; z < Dimensions.Z; z++)
      {
        for (int x = 0; x < Dimensions.X; x++)
          yield return new ValidatedPoint(new Vector3I(x, y, z));
      }
    }
  }

  private void InitializePathGraph()
  {
    _pathGraph.ReserveSpace(Dimensions.X * Dimensions.Y * Dimensions.Z);

    foreach (ValidatedPoint point in EnumerateBoardPoints())
      InitializePathGraphPoint(point);
  }

  private bool CanUsePathEndpoint(ValidatedPoint point, int movingUnitId)
  {
    BattleTileState tile = GetTile(point);
    if (!tile.IsWalkable)
      return false;
    if (!tile.IsOccupied)
      return true;

    return tile.HasOccupant(movingUnitId);
  }

  private long CoordinatesToPointId(ValidatedPoint point)
  {
    return point.X
      + ((long)Dimensions.X * point.Z)
      + ((long)Dimensions.X * Dimensions.Z * point.Y);
  }

  private long CoordinatesToPointId(Vector3I coordinates)
  {
    return coordinates.X
      + ((long)Dimensions.X * coordinates.Z)
      + ((long)Dimensions.X * Dimensions.Z * coordinates.Y);
  }

  public BattleTileState GetTile(ValidatedPoint point)
  {
    return _tiles[point.X, point.Y, point.Z];
  }

  private void OnTileTraversalStateChanged(ValidatedPoint point, BattleTileState tile)
  {
    ArgumentNullException.ThrowIfNull(tile);
    UpdatePathPointState(point, tile);
  }

  private void UpdatePathPointState(ValidatedPoint point, BattleTileState tile)
  {
    ArgumentNullException.ThrowIfNull(tile);
    _pathGraph.SetPointDisabled(CoordinatesToPointId(point), ShouldDisablePathPoint(tile));
  }

  private void InitializePathGraphPoint(ValidatedPoint point)
  {
    Vector3I coordinates = point.Raw;
    long pointId = CoordinatesToPointId(point);
    _pathGraph.AddPoint(pointId, BattleGridMath.CellToLocalCenter(coordinates));

    ConnectPathGraphPointToExistingNeighbor(pointId, coordinates + new Vector3I(-1, 0, 0));
    ConnectPathGraphPointToExistingNeighbor(pointId, coordinates + new Vector3I(0, 0, -1));
    ConnectPathGraphPointToExistingNeighbor(pointId, coordinates + new Vector3I(0, -1, 0));

    UpdatePathPointState(point, GetTile(point));
  }

  private void ConnectPathGraphPointToExistingNeighbor(long pointId, Vector3I neighborCoordinates)
  {
    if (!IsInBounds(neighborCoordinates))
      return;

    _pathGraph.ConnectPoints(pointId, CoordinatesToPointId(neighborCoordinates));
  }

  private static bool ShouldDisablePathPoint(BattleTileState tile)
  {
    return !tile.IsWalkable || tile.IsOccupied;
  }

  private ValidatedPoint PointIdToCoordinates(long pointId)
  {
    long layerSize = (long)Dimensions.X * Dimensions.Z;
    if (pointId < 0)
      throw new ArgumentOutOfRangeException(nameof(pointId), "Point id must be non-negative.");

    long y = pointId / layerSize;
    long remainder = pointId % layerSize;
    long z = remainder / Dimensions.X;
    long x = remainder % Dimensions.X;
    if (x >= Dimensions.X || y >= Dimensions.Y || z >= Dimensions.Z)
      throw new ArgumentOutOfRangeException(nameof(pointId), "Point id must map to a valid board coordinate.");

    return new ValidatedPoint(new Vector3I((int)x, (int)y, (int)z));
  }
}
