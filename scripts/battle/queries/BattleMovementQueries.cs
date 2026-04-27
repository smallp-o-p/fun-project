using Godot;
using System;
using System.Collections.Generic;

namespace FunProject.Battle;

public sealed class FindPathForUnit : BattleSessionQuery<BattleBoardState.ValidatedPoint[]>
{
  public const string Id = "find_path_for_unit";

  public BattleSession.BattleUnitHandle UnitHandle { get; }
  public Vector3I Destination { get; }

  public FindPathForUnit(BattleSession.BattleUnitHandle unitHandle, Vector3I destination)
    : base(Id)
  {
    ArgumentNullException.ThrowIfNull(unitHandle);
    UnitHandle = unitHandle;
    Destination = destination;
  }

  internal override Either<BattleQueryFailure, BattleBoardState.ValidatedPoint[]> Execute(BattleSession session)
  {
    ArgumentNullException.ThrowIfNull(session);

    Either<BattleQueryFailure, BattleUnitState> unitResult = new GetLivingUnit(UnitHandle).Execute(session);
    return unitResult.Match(
      Fail,
      unit =>
      {
        Option<BattleBoardState.ValidatedPoint> unitPointOption = session.GetUnitPosition(unit);
        Option<BattleBoardState.ValidatedPoint> destinationPointOption = session.Board.ValidatePoint(Destination);

        if (unitPointOption.IsNone)
          return Fail(BattleQueryFailureReason.InvalidTile, $"Unit {UnitHandle.UnitId} is not on the board.");
        if (destinationPointOption.IsNone)
          return Fail(BattleQueryFailureReason.InvalidTile, $"Destination {Destination} is outside the battle board.");

        BattleBoardState.ValidatedPoint unitPoint = unitPointOption.IfNone(default(BattleBoardState.ValidatedPoint));
        BattleBoardState.ValidatedPoint destinationPoint = destinationPointOption.IfNone(default(BattleBoardState.ValidatedPoint));

        return Succeed(session.Board.FindPath(unitPoint, destinationPoint, unit.UnitId));
      });
  }
}

public sealed class GetPossibleMoveTilesForUnit : BattleSessionQuery<IReadOnlyCollection<BattleBoardState.ValidatedPoint>>
{
  public const string Id = "get_possible_move_tiles_for_unit";

  public BattleSession.BattleUnitHandle UnitHandle { get; }
  public int ActionPointCostPerStep { get; }

  public GetPossibleMoveTilesForUnit(
    BattleSession.BattleUnitHandle unitHandle,
    int actionPointCostPerStep = BattleSession.DefaultMovementStepActionPointCost)
    : base(Id)
  {
    ArgumentNullException.ThrowIfNull(unitHandle);
    UnitHandle = unitHandle;
    if (actionPointCostPerStep < 0)
      throw new ArgumentOutOfRangeException(nameof(actionPointCostPerStep), "Action point cost cannot be negative.");

    ActionPointCostPerStep = actionPointCostPerStep;
  }

  internal override Either<BattleQueryFailure, IReadOnlyCollection<BattleBoardState.ValidatedPoint>> Execute(BattleSession session)
  {
    ArgumentNullException.ThrowIfNull(session);

    Either<BattleQueryFailure, BattleUnitState> unitResult = new GetLivingUnit(UnitHandle).Execute(session);
    return unitResult.Match(
      Fail,
      unit =>
      {
        if (unit.CurrentActionPoints < ActionPointCostPerStep)
          return Succeed([]);

        Option<BattleBoardState.ValidatedPoint> unitPointOption = session.GetUnitPosition(unit);
        if (unitPointOption.IsNone)
          return Fail(BattleQueryFailureReason.InvalidTile, $"Unit {UnitHandle.UnitId} is not on the board.");

        BattleBoardState.ValidatedPoint unitPoint = unitPointOption.IfNone(default(BattleBoardState.ValidatedPoint));

        List<BattleBoardState.ValidatedPoint> possibleTiles = [];
        foreach (BattleBoardState.ValidatedPoint candidatePoint in EnumerateCandidatePoints(session.Board, unit, unitPoint))
        {
          if (candidatePoint == unitPoint)
            continue;
          if (!session.Board.CanOccupy(candidatePoint))
            continue;

          BattleBoardState.ValidatedPoint[] path = session.Board.FindPath(unitPoint, candidatePoint, unit.UnitId);
          if (path.Length == 0)
            continue;

          int stepCount = path.Length - 1;
          if (CanPayMovementCost(unit, stepCount))
            possibleTiles.Add(candidatePoint);
        }

        return Succeed(possibleTiles);
      });
  }

  private IEnumerable<BattleBoardState.ValidatedPoint> EnumerateCandidatePoints(BattleBoardState board, BattleUnitState unit, BattleBoardState.ValidatedPoint unitPosition)
  {
    if (ActionPointCostPerStep == 0)
      return board.EnumerateBoardPoints();

    int maxSteps = unit.CurrentActionPoints / ActionPointCostPerStep;
    return EnumeratePointsWithinStepBound(board, unitPosition, maxSteps);
  }

  private static IEnumerable<BattleBoardState.ValidatedPoint> EnumeratePointsWithinStepBound(
    BattleBoardState board,
    BattleBoardState.ValidatedPoint origin,
    int maxSteps)
  {
    int minX = Math.Max(0, origin.X - maxSteps);
    int maxX = Math.Min(board.Dimensions.X - 1, origin.X + maxSteps);
    int minY = Math.Max(0, origin.Y - maxSteps);
    int maxY = Math.Min(board.Dimensions.Y - 1, origin.Y + maxSteps);
    int minZ = Math.Max(0, origin.Z - maxSteps);
    int maxZ = Math.Min(board.Dimensions.Z - 1, origin.Z + maxSteps);

    for (int y = minY; y <= maxY; y++)
    {
      for (int z = minZ; z <= maxZ; z++)
      {
        for (int x = minX; x <= maxX; x++)
        {
          Vector3I coordinates = new(x, y, z);
          if (BattleSession.GetGridDistance(origin.Raw, coordinates) > maxSteps)
            continue;

          Option<BattleBoardState.ValidatedPoint> point = board.ValidatePoint(coordinates);
          if (point.IsSome)
            yield return point.IfNone(default(BattleBoardState.ValidatedPoint));
        }
      }
    }
  }

  private bool CanPayMovementCost(BattleUnitState unit, int stepCount)
  {
    long totalActionPointCost = (long)stepCount * ActionPointCostPerStep;
    return totalActionPointCost <= unit.CurrentActionPoints;
  }
}
