#nullable enable
using Godot;
using System;
using System.Collections.Generic;

namespace FunProject.Battle;

public sealed class BattleBoardState
{
  private static readonly Vector3I[] AdjacentDirections =
  [
    new Vector3I(1, 0, 0),
    new Vector3I(-1, 0, 0),
    new Vector3I(0, 1, 0),
    new Vector3I(0, -1, 0),
    new Vector3I(0, 0, 1),
    new Vector3I(0, 0, -1),
  ];

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
          BattleTileState tile = new(new Vector3I(x, y, z));
          tile.TraversalStateChanged += OnTileTraversalStateChanged;
          _tiles[x, y, z] = tile;
        }
      }
    }

    InitializePathGraph();
  }

  public bool IsInBounds(Vector3I coordinates)
  {
    return coordinates.X >= 0
      && coordinates.Y >= 0
      && coordinates.Z >= 0
      && coordinates.X < Dimensions.X
      && coordinates.Y < Dimensions.Y
      && coordinates.Z < Dimensions.Z;
  }

  public BattleTileState? GetTileOrNull(Vector3I coordinates)
  {
    if (!IsInBounds(coordinates))
      return null;

    return _tiles[coordinates.X, coordinates.Y, coordinates.Z];
  }

  public BattleTileState GetTile(Vector3I coordinates)
  {
    if (!IsInBounds(coordinates))
      throw new ArgumentOutOfRangeException(nameof(coordinates));

    return GetTileInBounds(coordinates);
  }

  public bool TryPlaceOccupant(Vector3I coordinates, int unitId)
  {
    if (!IsInBounds(coordinates))
      return false;

    BattleTileState tile = GetTileInBounds(coordinates);
    return tile.TrySetOccupant(unitId);
  }

  public bool TryMoveOccupant(Vector3I source, Vector3I destination, int unitId)
  {
    if (!IsInBounds(source) || !IsInBounds(destination))
      return false;

    BattleTileState sourceTile = GetTileInBounds(source);
    BattleTileState destinationTile = GetTileInBounds(destination);
    if (sourceTile.OccupantUnitId != unitId)
      return false;
    if (source == destination)
      return true;
    if (!destinationTile.TrySetOccupant(unitId))
      return false;

    sourceTile.ClearOccupant();
    return true;
  }

  public bool TryClearOccupant(Vector3I coordinates, int unitId)
  {
    if (!IsInBounds(coordinates))
      return false;

    BattleTileState tile = GetTileInBounds(coordinates);
    if (tile.OccupantUnitId != unitId)
      return false;

    tile.ClearOccupant();
    return true;
  }

  public Vector3I[] FindPath(Vector3I source, Vector3I destination, int movingUnitId = 0)
  {
    if (!CanUsePathEndpoint(source, movingUnitId) || !CanUsePathEndpoint(destination, movingUnitId))
      return [];

    long sourceId = CoordinatesToPointId(source);
    long destinationId = CoordinatesToPointId(destination);
    BattleTileState sourceTile = GetTileInBounds(source);
    bool restoreSourceDisabled = false;

    if (sourceTile.OccupantUnitId == movingUnitId && _pathGraph.IsPointDisabled(sourceId))
    {
      _pathGraph.SetPointDisabled(sourceId, false);
      restoreSourceDisabled = true;
    }

    Vector3I[] path = System.Array.ConvertAll(_pathGraph.GetIdPath(sourceId, destinationId), PointIdToCoordinates);
    if (restoreSourceDisabled)
      _pathGraph.SetPointDisabled(sourceId, ShouldDisablePathPoint(sourceTile));

    return path;
  }

  public bool CanOccupy(Vector3I coordinates)
  {
    if (!IsInBounds(coordinates))
      return false;

    BattleTileState tile = GetTileInBounds(coordinates);
    return tile.IsWalkable && !tile.IsOccupied;
  }

  public bool IsAdjacent(Vector3I source, Vector3I destination)
  {
    Vector3I delta = source - destination;
    return Mathf.Abs(delta.X) + Mathf.Abs(delta.Y) + Mathf.Abs(delta.Z) == 1;
  }

  public IEnumerable<Vector3I> EnumerateAdjacentCoordinates(Vector3I coordinates)
  {
    foreach (Vector3I direction in AdjacentDirections)
    {
      Vector3I adjacent = coordinates + direction;
      if (IsInBounds(adjacent))
        yield return adjacent;
    }
  }

  public IEnumerable<Vector3I> EnumerateBoardCoordinates()
  {
    for (int y = 0; y < Dimensions.Y; y++)
    {
      for (int z = 0; z < Dimensions.Z; z++)
      {
        for (int x = 0; x < Dimensions.X; x++)
          yield return new Vector3I(x, y, z);
      }
    }
  }

  private void InitializePathGraph()
  {
    _pathGraph.ReserveSpace(Dimensions.X * Dimensions.Y * Dimensions.Z);

    foreach (Vector3I coordinates in EnumerateBoardCoordinates())
      InitializePathGraphPoint(coordinates);
  }

  private bool CanUsePathEndpoint(Vector3I coordinates, int movingUnitId)
  {
    if (!IsInBounds(coordinates))
      return false;

    BattleTileState tile = GetTileInBounds(coordinates);
    if (!tile.IsWalkable)
      return false;
    if (!tile.IsOccupied)
      return true;

    return tile.OccupantUnitId == movingUnitId;
  }

  private long CoordinatesToPointId(Vector3I coordinates)
  {
    return coordinates.X
      + ((long)Dimensions.X * coordinates.Z)
      + ((long)Dimensions.X * Dimensions.Z * coordinates.Y);
  }

  private BattleTileState GetTileInBounds(Vector3I coordinates)
  {
    return _tiles[coordinates.X, coordinates.Y, coordinates.Z];
  }

  private void OnTileTraversalStateChanged(BattleTileState tile)
  {
    ArgumentNullException.ThrowIfNull(tile);
    UpdatePathPointState(tile);
  }

  private void UpdatePathPointState(BattleTileState tile)
  {
    _pathGraph.SetPointDisabled(CoordinatesToPointId(tile.Coordinates), ShouldDisablePathPoint(tile));
  }

  private void InitializePathGraphPoint(Vector3I coordinates)
  {
    long pointId = CoordinatesToPointId(coordinates);
    _pathGraph.AddPoint(pointId, BattleGridMath.CellToLocalCenter(coordinates));

    ConnectPathGraphPointToExistingNeighbor(pointId, coordinates + new Vector3I(-1, 0, 0));
    ConnectPathGraphPointToExistingNeighbor(pointId, coordinates + new Vector3I(0, 0, -1));
    ConnectPathGraphPointToExistingNeighbor(pointId, coordinates + new Vector3I(0, -1, 0));

    UpdatePathPointState(GetTileInBounds(coordinates));
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

  private Vector3I PointIdToCoordinates(long pointId)
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

    return new Vector3I((int)x, (int)y, (int)z);
  }
}
