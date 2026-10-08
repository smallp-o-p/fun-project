using Godot;
using System;
using System.Collections.Generic;
using Vector3 = System.Numerics.Vector3;

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

  private readonly BattleTileState[] _tiles;
  private readonly Dictionary<ValidatedPoint, int> _occupants = [];
  private readonly Dictionary<ValidatedPoint, int> _objectOccupants = [];
  private readonly Dictionary<int, ValidatedPoint> _positionByUnit = [];
  private readonly Dictionary<int, ValidatedPoint> _positionByObject = [];

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
    int count = checked(dim.X * dim.Z * dim.Y);
    _tiles = new BattleTileState[count];
    // Dimension-only boards are fully walkable; missing authored map cells remain holes.
    if (map is null)
    {
      System.Array.Fill(_tiles, new BattleTileState { IsWalkable = true });
    }
    else if (map.TileFlags.Length != 0)
    {
      for (int i = 0; i < count; i++)
      {
        var flags = (BattleTileFlags)map.TileFlags[i];
        _tiles[i] = new BattleTileState
        {
          IsWalkable = (flags & BattleTileFlags.Walkable) != 0,
          BlocksLineOfSight = (flags & BattleTileFlags.BlocksLineOfSight) != 0,
          BlocksVerticalLineOfSight = (flags & BattleTileFlags.BlocksVerticalLineOfSight) != 0,
          Cover = new((CoverDirections)map.TileCoverDirections[i], map.TileCoverAmounts[i])
        };
      }
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
    if (!GetTile(point).IsWalkable || _occupants.ContainsKey(point) || _objectOccupants.ContainsKey(point))
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
    if (_occupants.ContainsKey(destination) || _objectOccupants.ContainsKey(destination))
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

  public Option<ValidatedPoint> FindObjectPosition(int objectId)
  {
    return _positionByObject.TryGetValue(objectId, out ValidatedPoint point) ? Some(point) : None;
  }

  /// <summary>
  /// Find a shortest path from source to destination via breadth-first search (all steps cost
  /// the same — the board has no terrain costs yet; if weighted movement ever arrives, replace
  /// this with a best-first search, keeping the neighbor rule below). Every tile the search
  /// enters — the destination included — must be occupiable (walkable and unoccupied); the
  /// source is seeded without testing, so an occupied source still paths out of it. The fixed
  /// <see cref="BattlePathSearch.AdjacentDirections"/> order makes the choice among equal-length paths
  /// deterministic.
  /// </summary>
  /// <param name="source"></param>
  /// <param name="destination"></param>
  /// <returns>A path of points from source to destination, or [] if no path can be found.</returns>
  public ValidatedPoint[] FindPath(ValidatedPoint source, ValidatedPoint destination)
  {
    return BattlePathSearch.FindPath(Dimensions, source, destination, ExactKnowledge);
  }

  private TraversalKnowledge ExactKnowledge(ValidatedPoint point) => CanOccupy(point) ? TraversalKnowledge.Clear : TraversalKnowledge.Blocked;

  public bool CanOccupy(ValidatedPoint point)
  {
    return GetTile(point).IsWalkable && !_occupants.ContainsKey(point) && !_objectOccupants.ContainsKey(point);
  }

  private bool IsOccupiable(Vector3I coordinates)
    => ValidatePoint(coordinates).Match(Some: CanOccupy, None: () => false);

  private bool CanStep(ValidatedPoint from, Vector3I direction) =>
    BattlePathSearch.StepKnowledge(Dimensions, from, direction, ExactKnowledge) != TraversalKnowledge.Blocked;

  public static double GetGridDistance(Vector3I source, Vector3I destination)
  {
    return Vector3.Distance(new Vector3(source.X, source.Y, source.Z),
      new Vector3(destination.X, destination.Y, destination.Z));
  }

  public static bool AreAdjacent(ValidatedPoint source, ValidatedPoint destination)
  {
    var distance = GetGridDistance(source.Raw, destination.Raw);
    return distance != 0 && distance <= Math.Sqrt(2);
  }

  /// <summary>
  /// True when any adjacent tile (the same neighborhood the path search walks) can be
  /// occupied right now — the cheap no-search proxy for "some move exists".
  /// </summary>
  public bool HasOccupiableNeighbor(ValidatedPoint point)
  {
    foreach (Vector3I direction in BattlePathSearch.AdjacentDirections)
    {
      if (IsOccupiable(point.Raw + direction) && CanStep(point, direction))
        return true;
    }

    return false;
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

  // Use ref locals when retaining a mutable cell; ordinary value assignment makes a snapshot.
  public ref BattleTileState GetTile(ValidatedPoint point)
  {
    return ref GetTileUnchecked(point.X, point.Y, point.Z);
  }

  // Unchecked Tile access, do NOT call this unless you've proven that the input is valid.
  internal ref BattleTileState GetTileUnchecked(int x, int y, int z)
  {
    return ref _tiles[x + Dimensions.X * (z + Dimensions.Z * y)];
  }

  public bool IsBlockedByObject(ValidatedPoint point)
    => _objectOccupants.ContainsKey(point);

  public bool TryPlaceObjectOccupant(ValidatedPoint point, int objectId)
  {
    if (!GetTile(point).IsWalkable || _occupants.ContainsKey(point) || _objectOccupants.ContainsKey(point))
      return false;

    _objectOccupants[point] = objectId;
    _positionByObject[objectId] = point;
    return true;
  }

  public bool TryClearObjectOccupant(ValidatedPoint point, int objectId)
  {
    if (!_objectOccupants.TryGetValue(point, out int occupant) || occupant != objectId)
      return false;

    _objectOccupants.Remove(point);
    _positionByObject.Remove(objectId);
    return true;
  }

  public IReadOnlyCollection<ValidatedPoint> GetReachableTiles(ValidatedPoint origin, int maxSteps)
  {
    return BattlePathSearch.GetReachableTiles(Dimensions, origin, maxSteps, ExactKnowledge)
      .AsValueEnumerable().ToArray();
  }
}
