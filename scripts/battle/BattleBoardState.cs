using Godot;
using System;
using System.Collections.Generic;

namespace FunProject.Battle;

public sealed class BattleBoardState
{
  public readonly struct ValidatedPoint : IEquatable<ValidatedPoint>
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

    public override bool Equals(object? obj)
    {
      return obj is ValidatedPoint other && Equals(other);
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
  // Managed adjacency for every existing path-graph point, kept in sync as points/edges are added.
  // The key set is exactly the set of AStar points (sparse: void cells have no entry), so GetReachableTiles
  // can walk connectivity without per-node Godot interop allocations.
  private readonly Dictionary<long, List<long>> _neighborsById = [];
  private readonly Dictionary<ValidatedPoint, int> _occupants = [];
  private readonly Dictionary<int, ValidatedPoint> _positionByUnit = [];

  public Vector3I Dimensions { get; }

  public BattleBoardState(Vector3I dim) : this(dim, null)
  {
  }

  public BattleBoardState(BattleMapData map) : this(map.Dimensions, map)
  {
  }

  private BattleBoardState(Vector3I dim, BattleMapData? map)
  {
    if (dim.X <= 0 || dim.Y <= 0 || dim.Z <= 0)
      throw new ArgumentOutOfRangeException(nameof(dim));

    Dimensions = dim;
    _tiles = new BattleTileState[Dimensions.X, Dimensions.Y, Dimensions.Z];

    foreach (ValidatedPoint point in EnumerateBoardPoints())
      _tiles[point.X, point.Y, point.Z] = new BattleTileState();

    if (map is not null)
      ApplyMapData(map);

    // Build the path graph once, after final walkability is known, so void cells never allocate a node.
    BuildPathGraph();
  }

  private void ApplyMapData(BattleMapData map)
  {
    foreach (ValidatedPoint point in EnumerateBoardPoints())
      GetTile(point).IsWalkable = false;

    foreach (var entry in map.Tiles)
    {
      BattleMapTileData data = entry.Value;
      if (data is null)
        continue;

      ValidatePoint(entry.Key).Match(
        point =>
        {
          BattleTileState tile = GetTile(point);
          tile.IsWalkable = data.Walkable;
          tile.BlocksLineOfSight = data.BlocksLineOfSight;
          tile.BlocksVerticalLineOfSight = data.BlocksVerticalLineOfSight;
          tile.Cover = new TileCover(data.CoverDirections, data.CoverAmount);
        },
        () =>
        {
          GD.PushError($"{nameof(BattleMapData)} tile at '{entry.Key}' is out of bounds for dimensions '{map.Dimensions}'.");
        });
    }
  }

  public void SetTileWalkable(ValidatedPoint point, bool walkable)
  {
    GetTile(point).IsWalkable = walkable;
    if (walkable)
      EnsurePathGraphPoint(point);

    UpdatePathPointState(point);
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

  public bool IsOccupied(ValidatedPoint point)
  {
    return _occupants.ContainsKey(point);
  }

  public Option<int> GetOccupant(ValidatedPoint point)
  {
    return _occupants.TryGetValue(point, out int unitId) ? Some(unitId) : None;
  }

  public bool TryPlaceOccupant(ValidatedPoint point, int unitId)
  {
    if (!GetTile(point).IsWalkable || _occupants.ContainsKey(point))
      return false;

    _occupants[point] = unitId;
    _positionByUnit[unitId] = point;
    UpdatePathPointState(point);
    return true;
  }

  public bool TryMoveOccupant(ValidatedPoint source, ValidatedPoint destination, int unitId)
  {
    if (!_occupants.TryGetValue(source, out int sourceOccupant) || sourceOccupant != unitId)
      return false;
    if (source.Equals(destination))
      return true;
    if (_occupants.ContainsKey(destination))
      return false;

    _occupants.Remove(source);
    _occupants[destination] = unitId;
    _positionByUnit[unitId] = destination;
    UpdatePathPointState(source);
    UpdatePathPointState(destination);
    return true;
  }

  public bool TryClearOccupant(ValidatedPoint point, int unitId)
  {
    if (!_occupants.TryGetValue(point, out int occupant) || occupant != unitId)
      return false;

    _occupants.Remove(point);
    _positionByUnit.Remove(unitId);
    UpdatePathPointState(point);
    return true;
  }

  public Option<ValidatedPoint> FindOccupantPosition(int unitId)
  {
    return _positionByUnit.TryGetValue(unitId, out ValidatedPoint point) ? Some(point) : None;
  }

  /// <summary>
  /// Find a path for a unit at tile X to destination
  /// </summary>
  /// <param name="movingUnitId"></param>
  /// <param name="destination"></param>
  /// <returns>A path of points to destination, or [] if no path can be found.</returns>
  public ValidatedPoint[] FindPath(int movingUnitId, ValidatedPoint destination)
  {
    if (!_positionByUnit.TryGetValue(movingUnitId, out ValidatedPoint source))
      return [];
    if (!CanUsePathEndpoint(source, movingUnitId) || !CanUsePathEndpoint(destination, movingUnitId))
      return [];

    long sourceId = CoordinatesToPointId(source);
    long destinationId = CoordinatesToPointId(destination);

    // The mover occupies (and thus disables) its own source node, and AStar3D offers no "treat
    // this start point as enabled for one query" option, so we clear that flag for the duration
    // of the query and restore it in finally — leaving no residue even if GetIdPath throws.
    // Assumes single-threaded access to the board (the whole battle runtime is single-threaded).
    _pathGraph.SetPointDisabled(sourceId, false);

    try
    {
      return System.Array.ConvertAll(_pathGraph.GetIdPath(sourceId, destinationId), PointIdToCoordinates);
    }
    finally
    {
      _pathGraph.SetPointDisabled(sourceId, ShouldDisablePathPoint(source));
    }
  }

  public bool CanOccupy(ValidatedPoint point)
  {
    return GetTile(point).IsWalkable && !_occupants.ContainsKey(point);
  }

  // The board's single definition of orthogonal adjacency; the path graph and every
  // adjacency question must share it so movement rules cannot drift.
  private static readonly Vector3I[] OrthogonalDirections =
  [
    new(-1, 0, 0), new(1, 0, 0), new(0, 0, -1), new(0, 0, 1), new(0, -1, 0), new(0, 1, 0),
  ];

  /// <summary>
  /// True when any adjacent tile (the same neighborhood the path graph connects) can be
  /// occupied right now — the cheap no-BFS proxy for "some move exists".
  /// </summary>
  public bool HasOccupiableNeighbor(ValidatedPoint point)
  {
    foreach (Vector3I direction in OrthogonalDirections)
    {
      bool occupiable = ValidatePoint(point.Raw + direction).Match(
        Some: CanOccupy,
        None: () => false);
      if (occupiable)
        return true;
    }

    return false;
  }

  public static int GetGridDistance(Vector3I source, Vector3I destination)
  {
    var delta = source - destination;
    return Mathf.Abs(delta.X) + Mathf.Abs(delta.Y) + Mathf.Abs(delta.Z);
  }

  public static bool AreAdjacent(ValidatedPoint source, ValidatedPoint destination)
  {
    return GetGridDistance(source.Raw, destination.Raw) == 1;
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

  private void BuildPathGraph()
  {
    // Sparse: only walkable cells become nodes. Void/hole cells get no node or edges, mirroring
    // the previous "non-walkable point is permanently disabled" behavior at a fraction of the cost.
    int walkableCount = 0;
    foreach (ValidatedPoint point in EnumerateBoardPoints())
    {
      if (GetTile(point).IsWalkable)
        walkableCount++;
    }

    _pathGraph.ReserveSpace(Math.Max(1, walkableCount));

    foreach (ValidatedPoint point in EnumerateBoardPoints())
    {
      if (GetTile(point).IsWalkable)
        AddPathGraphPoint(point);
    }
  }

  private bool CanUsePathEndpoint(ValidatedPoint point, int movingUnitId)
  {
    if (!GetTile(point).IsWalkable)
      return false;
    if (!_occupants.TryGetValue(point, out int occupant))
      return true;

    return occupant == movingUnitId;
  }

  private long CoordinatesToPointId(ValidatedPoint point) => CoordinatesToPointId(point.Raw);

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

  // Unchecked Tile access, do NOT call this unless you've proven that the input is valid.
  internal BattleTileState GetTileUnchecked(int x, int y, int z)
  {
    return _tiles[x, y, z];
  }

  private void UpdatePathPointState(ValidatedPoint point)
  {
    long pointId = CoordinatesToPointId(point);
    if (!_pathGraph.HasPoint(pointId))
      return;

    _pathGraph.SetPointDisabled(pointId, ShouldDisablePathPoint(point));
  }

  private void EnsurePathGraphPoint(ValidatedPoint point)
  {
    if (!_pathGraph.HasPoint(CoordinatesToPointId(point)))
      AddPathGraphPoint(point);
  }

  private void AddPathGraphPoint(ValidatedPoint point)
  {
    Vector3I coordinates = point.Raw;
    long pointId = CoordinatesToPointId(point);
    _pathGraph.AddPoint(pointId, ToPathGraphPosition(coordinates));
    _neighborsById[pointId] = [];

    // Connect to every in-bounds neighbor that already has a node. During construction only the
    // backward neighbors exist yet; for a point added later (SetTileWalkable) any of the six may.
    foreach (Vector3I direction in OrthogonalDirections)
      ConnectPathGraphPointToExistingNeighbor(pointId, coordinates + direction);

    UpdatePathPointState(point);
  }

  private static Vector3 ToPathGraphPosition(Vector3I coordinates)
  {
    return new Vector3(
      coordinates.X + 0.5f,
      coordinates.Y + 0.5f,
      coordinates.Z + 0.5f);
  }

  private void ConnectPathGraphPointToExistingNeighbor(long pointId, Vector3I neighborCoordinates)
  {
    if (!IsInBounds(neighborCoordinates))
      return;

    long neighborId = CoordinatesToPointId(neighborCoordinates);
    if (!_neighborsById.TryGetValue(neighborId, out List<long>? neighborList))
      return;

    _pathGraph.ConnectPoints(pointId, neighborId);
    _neighborsById[pointId].Add(neighborId);
    neighborList.Add(pointId);
  }

  private bool ShouldDisablePathPoint(ValidatedPoint point)
  {
    return !GetTile(point).IsWalkable || _occupants.ContainsKey(point);
  }

  public IReadOnlyCollection<ValidatedPoint> GetReachableTiles(ValidatedPoint origin, int maxSteps)
  {
    // Walks the cached path graph so connectivity and blocking stay defined in one place.
    // The origin is enqueued unconditionally: its own disabled state (the mover standing
    // on it) must not block the search, mirroring FindPath's source handling.
    List<ValidatedPoint> reachable = [];
    SysColGeneric.HashSet<long> visited = [CoordinatesToPointId(origin)];
    Queue<(long id, int steps)> frontier = new();
    frontier.Enqueue((CoordinatesToPointId(origin), 0));

    while (frontier.Count > 0)
    {
      var (currentId, steps) = frontier.Dequeue();
      if (steps >= maxSteps)
        continue;
      if (!_neighborsById.TryGetValue(currentId, out List<long>? neighbors))
        continue;

      foreach (long neighborId in neighbors)
      {
        if (!visited.Add(neighborId))
          continue;
        if (_pathGraph.IsPointDisabled(neighborId))
          continue;

        reachable.Add(PointIdToCoordinates(neighborId));
        frontier.Enqueue((neighborId, steps + 1));
      }
    }

    return reachable;
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
