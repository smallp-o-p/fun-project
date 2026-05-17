using FunProject.Combatants;
using Godot;
using LanguageExt.UnsafeValueAccess;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FunProject.Battle;

internal sealed class BattleVisibilitySystem
{
  private const float RayStepEpsilon = 0.0001f;
  private static readonly Vector3 TileCenterOffset = new(0.5f, 0.5f, 0.5f);

  public void Refresh(BattleSession session)
  {
    ArgumentNullException.ThrowIfNull(session);

    var board = session.Board;
    foreach (var unit in session.Units)
      unit.ClearVisibility();

    var livingUnits = session.AliveUnits.ToArray();
    Dictionary<BattleBoardState.ValidatedPoint, BattleUnitState> livingUnitsByPosition = GetLivingUnitsByPosition(session, livingUnits);

    foreach (var observer in livingUnits)
    {
      SysColGeneric.HashSet<BattleBoardState.ValidatedPoint> observerVisibleTiles = [];
      Option<BattleBoardState.ValidatedPoint> observerPointOption = session.GetUnitPosition(observer);
      if (observerPointOption.IsNone)
        continue;
      BattleBoardState.ValidatedPoint observerPosition = observerPointOption.IfNone(default(BattleBoardState.ValidatedPoint));

      foreach (var targetTile in EnumerateTileCandidates(board, observer, observerPosition))
      {
        if (CanSeeTile(board, observer, observerPosition, targetTile))
        {
          observerVisibleTiles.Add(targetTile);
          observer.AddVisibleTile(targetTile);
        }
      }

      session.MarkTilesExplored(observer.Side, observerVisibleTiles);

      foreach (var visibleTile in observerVisibleTiles)
      {
        if (!livingUnitsByPosition.TryGetValue(visibleTile, out var target))
          continue;
        if (target.Id == observer.Id)
          continue;

        observer.AddVisibleUnit(target);
      }
    }
  }

  private static Dictionary<BattleBoardState.ValidatedPoint, BattleUnitState> GetLivingUnitsByPosition(
    BattleSession session,
    IReadOnlyCollection<BattleUnitState> livingUnits)
  {
    ArgumentNullException.ThrowIfNull(session);
    ArgumentNullException.ThrowIfNull(livingUnits);

    Dictionary<BattleBoardState.ValidatedPoint, BattleUnitState> livingUnitsByPosition = [];
    foreach (var unit in livingUnits)
    {
      Option<BattleBoardState.ValidatedPoint> unitPointOption = session.GetUnitPosition(unit);
      if (unitPointOption.IsNone)
        continue;

      livingUnitsByPosition[unitPointOption.IfNone(default(BattleBoardState.ValidatedPoint))] = unit;
    }

    return livingUnitsByPosition;
  }

  private static IEnumerable<BattleBoardState.ValidatedPoint> EnumerateTileCandidates(
    BattleBoardState board,
    BattleUnitState observer,
    BattleBoardState.ValidatedPoint observerPosition)
  {
    ArgumentNullException.ThrowIfNull(board);
    ArgumentNullException.ThrowIfNull(observer);

    if (observer.Vision <= 0)
      yield break;

    foreach (var point in board.EnumerateBoardPoints())
    {
      if (Math.Abs(point.X - observerPosition.X) > observer.Vision)
        continue;
      if (Math.Abs(point.Y - observerPosition.Y) > observer.Vision)
        continue;
      if (Math.Abs(point.Z - observerPosition.Z) > observer.Vision)
        continue;

      yield return point;
    }
  }

  private static bool CanSeeTile(
    BattleBoardState board,
    BattleUnitState observer,
    BattleBoardState.ValidatedPoint observerPosition,
    BattleBoardState.ValidatedPoint targetTile)
  {
    ArgumentNullException.ThrowIfNull(board);
    ArgumentNullException.ThrowIfNull(observer);

    if (observer.IsDead)
      return false;

    if (targetTile != observerPosition && board.GetTile(targetTile).BlocksLineOfSight)
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

  private static Vector3 ToWorldPoint(BattleBoardState.ValidatedPoint cell)
  {
    return new Vector3(cell.X + TileCenterOffset.X, cell.Y + TileCenterOffset.Y, cell.Z + TileCenterOffset.Z);
  }

  private static bool HasLineOfSight(BattleBoardState board, Vector3 origin, Vector3 target)
  {
    ArgumentNullException.ThrowIfNull(board);

    Option<BattleBoardState.ValidatedPoint> originPointOption = board.ValidatePoint(origin);
    Option<BattleBoardState.ValidatedPoint> targetPointOption = board.ValidatePoint(target);
    if (originPointOption.IsNone || targetPointOption.IsNone)
      return false;

    BattleBoardState.ValidatedPoint originPoint = originPointOption.Value();
    BattleBoardState.ValidatedPoint targetPoint = targetPointOption.Value();
    if (originPoint == targetPoint)
      return true;

    var delta = target - origin;
    int stepX = Math.Sign(delta.X);
    int stepY = Math.Sign(delta.Y);
    int stepZ = Math.Sign(delta.Z);

    float tMaxX = GetInitialTraversalDistance(origin.X, delta.X, originPoint.X);
    float tMaxY = GetInitialTraversalDistance(origin.Y, delta.Y, originPoint.Y);
    float tMaxZ = GetInitialTraversalDistance(origin.Z, delta.Z, originPoint.Z);
    float tDeltaX = GetTraversalDelta(delta.X);
    float tDeltaY = GetTraversalDelta(delta.Y);
    float tDeltaZ = GetTraversalDelta(delta.Z);

    int currentX = originPoint.X;
    int currentY = originPoint.Y;
    int currentZ = originPoint.Z;

    while (currentX != targetPoint.X || currentY != targetPoint.Y || currentZ != targetPoint.Z)
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

      Option<BattleBoardState.ValidatedPoint> currentPointOption = board.ValidatePoint(new(currentX, currentY, currentZ));
      if (currentPointOption.IsNone)
        return false;
      BattleBoardState.ValidatedPoint currentPoint = currentPointOption.IfNone(default(BattleBoardState.ValidatedPoint));
      if (currentPoint == targetPoint)
        return true;

      if (board.GetTile(currentPoint).BlocksLineOfSight)
        return false;
    }

    return true;
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
