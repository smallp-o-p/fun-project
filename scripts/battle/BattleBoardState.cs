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
  private readonly Dictionary<ValidatedPoint, int> _occupants = [];
  private readonly Dictionary<int, ValidatedPoint> _positionByUnit = [];

  public Vector3I Dimensions { get; }

  public BattleBoardState(Vector3I dim) : this(dim, null)
  {
  }

  public BattleBoardState(BattleMapData map)
    : this(new Vector3I(map.Dimensions.X, map.Dimensions.Y, map.Dimensions.Z), map)
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

      ValidatePoint(new Vector3I(entry.Key.X, entry.Key.Y, entry.Key.Z)).Match(
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
    return true;
  }

  public bool TryClearOccupant(ValidatedPoint point, int unitId)
  {
    if (!_occupants.TryGetValue(point, out int occupant) || occupant != unitId)
      return false;

    _occupants.Remove(point);
    _positionByUnit.Remove(unitId);
    return true;
  }

  public Option<ValidatedPoint> FindOccupantPosition(int unitId)
  {
    return _positionByUnit.TryGetValue(unitId, out ValidatedPoint point) ? Some(point) : None;
  }

  /// <summary>
  /// Find a shortest path from source to destination via breadth-first search (all steps cost
  /// the same — the board has no terrain costs yet; if weighted movement ever arrives, replace
  /// this with a best-first search, keeping the neighbor rule below). Every tile the search
  /// enters — the destination included — must be occupiable (walkable and unoccupied); the
  /// source is seeded without testing, so an occupied source still paths out of it. The fixed
  /// <see cref="OrthogonalDirections"/> order makes the choice among equal-length paths
  /// deterministic.
  /// </summary>
  /// <param name="source"></param>
  /// <param name="destination"></param>
  /// <returns>A path of points from source to destination, or [] if no path can be found.</returns>
  public ValidatedPoint[] FindPath(ValidatedPoint source, ValidatedPoint destination)
  {
    if (source == destination)
      return [source];
    if (!CanOccupy(destination))
      return [];

    Dictionary<ValidatedPoint, ValidatedPoint> parentByNode = [];
    Queue<ValidatedPoint> frontier = new();
    parentByNode[source] = source;
    frontier.Enqueue(source);

    while (frontier.Count > 0)
    {
      ValidatedPoint current = frontier.Dequeue();
      if (current == destination)
        return ReconstructPath(parentByNode, source, destination);

      foreach (Vector3I direction in OrthogonalDirections)
      {
        Vector3I neighborCoordinates = current.Raw + direction;
        if (!IsInBounds(neighborCoordinates))
          continue;

        var neighbor = new ValidatedPoint(neighborCoordinates);
        if (parentByNode.ContainsKey(neighbor))
          continue;
        if (!CanOccupy(neighbor))
          continue;

        parentByNode[neighbor] = current;
        frontier.Enqueue(neighbor);
      }
    }

    return [];
  }

  private static ValidatedPoint[] ReconstructPath(
    Dictionary<ValidatedPoint, ValidatedPoint> parentByNode,
    ValidatedPoint source,
    ValidatedPoint destination)
  {
    List<ValidatedPoint> reversed = [];
    for (ValidatedPoint current = destination; current != source; current = parentByNode[current])
      reversed.Add(current);
    reversed.Add(source);

    return reversed.AsValueEnumerable().Reverse().ToArray();
  }

  public bool CanOccupy(ValidatedPoint point)
  {
    return GetTile(point).IsWalkable && !_occupants.ContainsKey(point);
  }

  // The board's single definition of orthogonal adjacency; the path search and every
  // adjacency question must share it so movement rules cannot drift. The order is load-bearing:
  // searches visit neighbors in this order, which fixes the tie-break among equal-cost paths.
  private static readonly Vector3I[] OrthogonalDirections =
  [
    new(-1, 0, 0), new(1, 0, 0), new(0, 0, -1), new(0, 0, 1), new(0, -1, 0), new(0, 1, 0),
  ];

  /// <summary>
  /// True when any adjacent tile (the same neighborhood the path search walks) can be
  /// occupied right now — the cheap no-search proxy for "some move exists".
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
    return Math.Abs(delta.X) + Math.Abs(delta.Y) + Math.Abs(delta.Z);
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

  public BattleTileState GetTile(ValidatedPoint point)
  {
    return _tiles[point.X, point.Y, point.Z];
  }

  // Unchecked Tile access, do NOT call this unless you've proven that the input is valid.
  internal BattleTileState GetTileUnchecked(int x, int y, int z)
  {
    return _tiles[x, y, z];
  }

  public IReadOnlyCollection<ValidatedPoint> GetReachableTiles(ValidatedPoint origin, int maxSteps)
  {
    // Bounded-depth flood over occupiable tiles, using the same neighbor rule FindPath searches.
    // The origin seeds the search unconditionally — its own occupation (the mover standing on
    // it) must not block the search — but is not itself part of the result.
    List<ValidatedPoint> reachable = [];
    SysColGeneric.HashSet<ValidatedPoint> visited = [origin];
    Queue<(ValidatedPoint Point, int Steps)> frontier = new();
    frontier.Enqueue((origin, 0));

    while (frontier.Count > 0)
    {
      var (current, steps) = frontier.Dequeue();
      if (steps >= maxSteps)
        continue;

      foreach (Vector3I direction in OrthogonalDirections)
      {
        Vector3I neighborCoordinates = current.Raw + direction;
        if (!IsInBounds(neighborCoordinates))
          continue;

        var neighbor = new ValidatedPoint(neighborCoordinates);
        if (!visited.Add(neighbor))
          continue;
        if (!CanOccupy(neighbor))
          continue;

        reachable.Add(neighbor);
        frontier.Enqueue((neighbor, steps + 1));
      }
    }

    return reachable;
  }
}
