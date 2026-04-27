using FunProject.Combatants;
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FunProject.Battle;

internal sealed class BattleVisibilitySystem
{
  private const float RayStepEpsilon = 0.0001f;
  private static readonly Vector3 TileCenterOffset = new(0.5f, 0.5f, 0.5f);

  public BattleVisibilitySnapshot Build(BattleSession session)
  {
    ArgumentNullException.ThrowIfNull(session);

    var board = session.Board;
    var livingUnits = session.AliveUnits.ToArray();
    Dictionary<Vector3I, BattleUnitState> livingUnitsByPosition = GetLivingUnitsByPosition(session, livingUnits);
    Dictionary<Faction, SysColGeneric.HashSet<Vector3I>> visibleTilesByFaction = [];
    Dictionary<Faction, SysColGeneric.HashSet<int>> visibleForeignUnitsByFaction = [];
    Dictionary<int, IReadOnlySet<int>> visibleUnitsByObserver = [];

    foreach (var faction in GetParticipatingFactions(session))
    {
      visibleTilesByFaction[faction] = [];
      visibleForeignUnitsByFaction[faction] = [];
    }

    foreach (var observer in livingUnits)
    {
      SysColGeneric.HashSet<Vector3I> observerVisibleTiles = [];
      SysColGeneric.HashSet<int> visibleUnits = [];
      visibleUnitsByObserver[observer.UnitId] = visibleUnits;
      Option<BattleBoardState.ValidatedPoint> observerPointOption = session.GetUnitPosition(observer);
      if (observerPointOption.IsNone)
        continue;
      Vector3I observerPosition = observerPointOption.IfNone(default(BattleBoardState.ValidatedPoint)).Raw;

      foreach (var targetTile in EnumerateTileCandidates(board, observer, observerPosition))
      {
        if (CanSeeTile(board, observer, observerPosition, targetTile))
          observerVisibleTiles.Add(targetTile);
      }

      visibleTilesByFaction[observer.Side].UnionWith(observerVisibleTiles);

      foreach (var visibleTile in observerVisibleTiles)
      {
        if (!livingUnitsByPosition.TryGetValue(visibleTile, out var target))
          continue;
        if (target.UnitId == observer.UnitId)
          continue;

        visibleUnits.Add(target.UnitId);
        if (target.Side != observer.Side)
          visibleForeignUnitsByFaction[observer.Side].Add(target.UnitId);
      }
    }

    Dictionary<Faction, BattleFactionVisibilityState> factionStates = [];
    foreach (var faction in visibleTilesByFaction.Keys)
    {
      var visibleTiles = visibleTilesByFaction[faction];
      factionStates[faction] = new BattleFactionVisibilityState(
        visibleTiles,
        visibleTiles,
        visibleForeignUnitsByFaction[faction]);
    }

    return new BattleVisibilitySnapshot(factionStates, visibleUnitsByObserver);
  }

  private static Dictionary<Vector3I, BattleUnitState> GetLivingUnitsByPosition(
    BattleSession session,
    IReadOnlyCollection<BattleUnitState> livingUnits)
  {
    ArgumentNullException.ThrowIfNull(session);
    ArgumentNullException.ThrowIfNull(livingUnits);

    Dictionary<Vector3I, BattleUnitState> livingUnitsByPosition = [];
    foreach (var unit in livingUnits)
    {
      Option<BattleBoardState.ValidatedPoint> unitPointOption = session.GetUnitPosition(unit);
      if (unitPointOption.IsNone)
        continue;

      livingUnitsByPosition[unitPointOption.IfNone(default(BattleBoardState.ValidatedPoint)).Raw] = unit;
    }

    return livingUnitsByPosition;
  }

  private static IEnumerable<Faction> GetParticipatingFactions(BattleSession session)
  {
    ArgumentNullException.ThrowIfNull(session);
    return [.. session.GlobalFactionTurnOrder];
  }

  private static IEnumerable<Vector3I> EnumerateTileCandidates(BattleBoardState board, BattleUnitState observer, Vector3I observerPosition)
  {
    ArgumentNullException.ThrowIfNull(board);
    ArgumentNullException.ThrowIfNull(observer);

    if (observer.Vision <= 0)
      yield break;

    int minX = Math.Max(0, observerPosition.X - observer.Vision);
    int maxX = Math.Min(board.Dimensions.X - 1, observerPosition.X + observer.Vision);
    int minY = Math.Max(0, observerPosition.Y - observer.Vision);
    int maxY = Math.Min(board.Dimensions.Y - 1, observerPosition.Y + observer.Vision);
    int minZ = Math.Max(0, observerPosition.Z - observer.Vision);
    int maxZ = Math.Min(board.Dimensions.Z - 1, observerPosition.Z + observer.Vision);

    for (int y = minY; y <= maxY; y++)
    {
      for (int z = minZ; z <= maxZ; z++)
      {
        for (int x = minX; x <= maxX; x++)
          yield return new Vector3I(x, y, z);
      }
    }
  }

  private static bool CanSeeTile(BattleBoardState board, BattleUnitState observer, Vector3I observerPosition, Vector3I targetTile)
  {
    ArgumentNullException.ThrowIfNull(board);
    ArgumentNullException.ThrowIfNull(observer);

    if (observer.IsDead)
      return false;

    Option<BattleBoardState.ValidatedPoint> observerCell = board.ValidatePoint(observerPosition);
    Option<BattleBoardState.ValidatedPoint> targetCell = board.ValidatePoint(targetTile);

    if (observerCell.IsNone || targetCell.IsNone)
      return false;
    if (targetTile != observerPosition && targetCell.Match(point => board.GetTile(point).BlocksLineOfSight, () => false))
      return false;

    var observerPoint = ToWorldPoint(observerPosition);
    var targetPoint = ToWorldPoint(targetTile);
    if (!IsWithinVisionRange(observer, observerPoint, targetPoint))
      return false;

    return HasLineOfSight(board, observerPoint, targetPoint);
  }

  private static bool IsWithinVisionRange(BattleUnitState observer, Vector3 observerPoint, Vector3 targetPoint)
  {
    ArgumentNullException.ThrowIfNull(observer);
    if (observer.Vision <= 0)
      return false;

    return observerPoint.DistanceTo(targetPoint) <= observer.Vision;
  }

  private static Vector3 ToWorldPoint(Vector3I cell)
  {
    return new Vector3(cell.X + TileCenterOffset.X, cell.Y + TileCenterOffset.Y, cell.Z + TileCenterOffset.Z);
  }

  private static bool HasLineOfSight(BattleBoardState board, Vector3 origin, Vector3 target)
  {
    ArgumentNullException.ThrowIfNull(board);

    var originCell = ToTileCoordinates(origin);
    var targetCell = ToTileCoordinates(target);
    Option<BattleBoardState.ValidatedPoint> originPoint = board.ValidatePoint(originCell);
    Option<BattleBoardState.ValidatedPoint> targetPoint = board.ValidatePoint(targetCell);
    if (originPoint.IsNone || targetPoint.IsNone)
      return false;
    if (originCell == targetCell)
      return true;

    var delta = target - origin;
    int stepX = Math.Sign(delta.X);
    int stepY = Math.Sign(delta.Y);
    int stepZ = Math.Sign(delta.Z);

    float tMaxX = GetInitialTraversalDistance(origin.X, delta.X, originCell.X);
    float tMaxY = GetInitialTraversalDistance(origin.Y, delta.Y, originCell.Y);
    float tMaxZ = GetInitialTraversalDistance(origin.Z, delta.Z, originCell.Z);
    float tDeltaX = GetTraversalDelta(delta.X);
    float tDeltaY = GetTraversalDelta(delta.Y);
    float tDeltaZ = GetTraversalDelta(delta.Z);

    int currentX = originCell.X;
    int currentY = originCell.Y;
    int currentZ = originCell.Z;

    while (currentX != targetCell.X || currentY != targetCell.Y || currentZ != targetCell.Z)
    {
      float nextCrossing = Mathf.Min(tMaxX, Mathf.Min(tMaxY, tMaxZ));
      if (float.IsPositiveInfinity(nextCrossing))
        return true;

      if (tMaxX <= nextCrossing + RayStepEpsilon)
      {
        currentX += stepX;
        tMaxX += tDeltaX;
      }

      if (tMaxY <= nextCrossing + RayStepEpsilon)
      {
        currentY += stepY;
        tMaxY += tDeltaY;
      }

      if (tMaxZ <= nextCrossing + RayStepEpsilon)
      {
        currentZ += stepZ;
        tMaxZ += tDeltaZ;
      }

      var currentCell = new Vector3I(currentX, currentY, currentZ);
      Option<BattleBoardState.ValidatedPoint> currentPointOption = board.ValidatePoint(currentCell);
      if (currentPointOption.IsNone)
        return false;
      BattleBoardState.ValidatedPoint currentPoint = currentPointOption.IfNone(default(BattleBoardState.ValidatedPoint));
      if (currentCell == targetCell)
        return true;

      if (board.GetTile(currentPoint).BlocksLineOfSight)
        return false;
    }

    return true;
  }

  private static Vector3I ToTileCoordinates(Vector3 point)
  {
    return new Vector3I(
      Mathf.FloorToInt(point.X),
      Mathf.FloorToInt(point.Y),
      Mathf.FloorToInt(point.Z));
  }

  private static float GetInitialTraversalDistance(float originComponent, float deltaComponent, int originCell)
  {
    if (Mathf.IsZeroApprox(deltaComponent))
      return float.PositiveInfinity;

    if (deltaComponent > 0f)
      return (originCell + 1f - originComponent) / deltaComponent;

    return (originComponent - originCell) / -deltaComponent;
  }

  private static float GetTraversalDelta(float deltaComponent)
  {
    if (Mathf.IsZeroApprox(deltaComponent))
      return float.PositiveInfinity;

    return 1f / Mathf.Abs(deltaComponent);
  }
}
