using FunProject.Combatants;
using Godot;
using LanguageExt.UnsafeValueAccess;
using System;
using System.Collections.Generic;

namespace FunProject.Battle;

internal sealed class VisibilityService
{
  private static readonly IReadOnlySet<BattleBoardState.ValidatedPoint> EmptyTileSet = new SysColGeneric.HashSet<BattleBoardState.ValidatedPoint>();
  private static readonly Vector3I[] OrthogonalDirections =
  [
    new(1, 0, 0),
    new(-1, 0, 0),
    new(0, 0, 1),
    new(0, 0, -1),
  ];
  private static readonly Vector3I[] AdjacentDiagonalOffsets =
  [
    new(-1, 0, -1),
    new(-1, 0, 1),
    new(1, 0, -1),
    new(1, 0, 1),
  ];

  private readonly Dictionary<Faction, SysColGeneric.HashSet<BattleBoardState.ValidatedPoint>> _exploredTilesByFaction = [];

  internal IReadOnlySet<BattleBoardState.ValidatedPoint> GetExploredTiles(Faction side)
  {
    ArgumentNullException.ThrowIfNull(side);
    return _exploredTilesByFaction.TryGetValue(side, out var tiles) ? tiles : EmptyTileSet;
  }

  internal void Refresh(
    BattleBoardState board,
    IReadOnlyList<BattleUnitState> allUnits,
    IEnumerable<BattleUnitState> aliveUnits)
  {
    ArgumentNullException.ThrowIfNull(board);
    ArgumentNullException.ThrowIfNull(allUnits);
    ArgumentNullException.ThrowIfNull(aliveUnits);

    foreach (var unit in allUnits)
      unit.ClearVisibility();

    foreach (var observer in aliveUnits)
      RecomputeObserver(board, allUnits, observer);
  }

  // Incremental counterpart to Refresh, applied after an occupancy change that touches only a
  // known set of units (a move/spawn/death). Line-of-sight blocking is a tile property and the
  // position/vision of every UNaffected observer is unchanged, so their visible-TILE sets are
  // invariant under another unit moving, spawning, or dying. Therefore we only:
  //   (1) fully recompute each affected unit's own visible tiles + units (and union its newly
  //       seen tiles into its faction's explored memory, exactly as a full pass would), and
  //   (2) refresh, for each unaffected alive observer, whether every affected unit now belongs
  //       to its visible-UNIT set (the only membership that can flip is that of a unit whose own
  //       cell changed or that left the board), leaving all other memberships untouched.
  // The resulting per-unit sets and per-faction explored memory are byte-identical to a full
  // Refresh against the same board state.
  internal void RefreshAffected(
    BattleBoardState board,
    IReadOnlyList<BattleUnitState> allUnits,
    IEnumerable<BattleUnitState> aliveUnits,
    IReadOnlySet<BattleUnitState> affectedUnits)
  {
    ArgumentNullException.ThrowIfNull(board);
    ArgumentNullException.ThrowIfNull(allUnits);
    ArgumentNullException.ThrowIfNull(aliveUnits);
    ArgumentNullException.ThrowIfNull(affectedUnits);

    foreach (var affected in affectedUnits)
    {
      affected.ClearVisibility();
      // A living unit is always indexed on the board (Refresh enforces the same invariant);
      // a dead/removed unit sees nothing, so its just-cleared sets are already correct.
      if (affected.IsAlive)
        RecomputeObserver(board, allUnits, affected);
    }

    foreach (var observer in aliveUnits)
    {
      if (affectedUnits.Contains(observer))
        continue;

      foreach (var affected in affectedUnits)
      {
        if (ReferenceEquals(affected, observer))
          continue;

        bool visible = affected.IsAlive
          && board.FindOccupantPosition(affected.Id).Match(
               position => observer.VisibleTiles.Contains(position),
               () => false);
        if (visible)
          observer.AddVisibleUnit(affected);
        else
          observer.RemoveVisibleUnit(affected);
      }
    }
  }

  private void RecomputeObserver(
    BattleBoardState board,
    IReadOnlyList<BattleUnitState> allUnits,
    BattleUnitState observer)
  {
    var observerPositionOption = board.FindOccupantPosition(observer.Id);
    if (observerPositionOption.IsNone)
      throw new InvalidOperationException($"Living unit {observer.Id} is missing from the session position index.");
    var observerPosition = observerPositionOption.Value();

    SysColGeneric.HashSet<BattleBoardState.ValidatedPoint> observerVisibleTiles = GetVisibleTiles(board, observer, observerPosition);
    foreach (var visibleTile in observerVisibleTiles)
      observer.AddVisibleTile(visibleTile);

    MarkTilesExplored(observer.Side, observerVisibleTiles);

    foreach (var visibleTile in observerVisibleTiles)
    {
      var occupantId = board.GetOccupant(visibleTile);
      if (occupantId.IsNone)
        continue;
      var target = allUnits[occupantId.Value()];
      if (ReferenceEquals(target, observer) || target.IsDead)
        continue;

      observer.AddVisibleUnit(target);
    }
  }

  private void MarkTilesExplored(Faction side, IEnumerable<BattleBoardState.ValidatedPoint> tiles)
  {
    ArgumentNullException.ThrowIfNull(side);
    ArgumentNullException.ThrowIfNull(tiles);

    if (!_exploredTilesByFaction.TryGetValue(side, out var exploredTiles))
    {
      exploredTiles = [];
      _exploredTilesByFaction.Add(side, exploredTiles);
    }

    exploredTiles.UnionWith(tiles);
  }

  private SysColGeneric.HashSet<BattleBoardState.ValidatedPoint> GetVisibleTiles(
    BattleBoardState board,
    BattleUnitState observer,
    BattleBoardState.ValidatedPoint observerPosition)
  {
    ArgumentNullException.ThrowIfNull(observer);

    Queue<BattleBoardState.ValidatedPoint> frontier = new([observerPosition]);
    SysColGeneric.HashSet<BattleBoardState.ValidatedPoint> visited = [];
    SysColGeneric.HashSet<BattleBoardState.ValidatedPoint> visibleTiles = [];

    while (frontier.Count > 0)
    {
      var current = frontier.Dequeue();
      if (!visited.Add(current))
        continue;
      if (!IsWithinSameLevelVisionRange(observerPosition, current, observer.Vision))
        continue;

      visibleTiles.Add(current);
      bool currentBlocksLineOfSight = current != observerPosition && board.GetTile(current).BlocksLineOfSight;
      if (!currentBlocksLineOfSight)
      {
        AddAdjacentDiagonalVisibleTiles(board, observerPosition, current, observer.Vision, visibleTiles);
        foreach (var neighbor in EnumerateOrthogonalNeighbors(board, current))
          frontier.Enqueue(neighbor);
      }
    }

    return visibleTiles;
  }

  private IEnumerable<BattleBoardState.ValidatedPoint> EnumerateOrthogonalNeighbors(BattleBoardState board, BattleBoardState.ValidatedPoint point)
  {
    foreach (var direction in OrthogonalDirections)
    {
      var neighborOption = board.ValidatePoint(point.Raw + direction);
      if (neighborOption.IsSome)
        yield return neighborOption.Value();
    }
  }

  private void AddAdjacentDiagonalVisibleTiles(
    BattleBoardState board,
    BattleBoardState.ValidatedPoint observerPosition,
    BattleBoardState.ValidatedPoint visibleTile,
    int vision,
    SysColGeneric.HashSet<BattleBoardState.ValidatedPoint> visibleTiles)
  {
    foreach (var offset in AdjacentDiagonalOffsets)
    {
      board.ValidatePoint(visibleTile.Raw + offset).IfSome((diagonal) =>
      {
        if (IsWithinSameLevelVisionRange(observerPosition, diagonal, vision) && HasOpenDiagonalSide(board, visibleTile, offset))
          visibleTiles.Add(diagonal);
      });
    }
  }

  private bool HasOpenDiagonalSide(BattleBoardState board, BattleBoardState.ValidatedPoint source, Vector3I diagonalOffset)
  {
    return IsOpenSide(board, source.Raw + new Vector3I(diagonalOffset.X, 0, 0))
      || IsOpenSide(board, source.Raw + new Vector3I(0, 0, diagonalOffset.Z));
  }

  private bool IsOpenSide(BattleBoardState board, Vector3I coordinates)
  {
    return board.ValidatePoint(coordinates).Match(
      side => !board.GetTile(side).BlocksLineOfSight,
      () => false);
  }

  private static bool IsWithinSameLevelVisionRange(
    BattleBoardState.ValidatedPoint observerPosition,
    BattleBoardState.ValidatedPoint target,
    int vision)
  {
    if (target == observerPosition)
      return true;
    if (vision < 0 || target.Y != observerPosition.Y)
      return false;

    Vector3I delta = target.Raw - observerPosition.Raw;
    return delta.LengthSquared() <= vision * vision;
  }
}
