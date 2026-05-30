using Godot;
using System;
using System.Collections.Generic;

namespace FunProject.Battle;

public sealed class FindPathForUnit : BattleSessionQuery<BattleBoardState.ValidatedPoint[]>
{
  public const string Id = "find_path_for_unit";

  public BattleUnitState Unit { get; }
  public Vector3I Destination { get; }

  public FindPathForUnit(BattleUnitState unit, Vector3I destination)
    : base(Id)
  {
    ArgumentNullException.ThrowIfNull(unit);
    Unit = unit;
    Destination = destination;
  }

  internal override Either<BattleQueryFailure, BattleBoardState.ValidatedPoint[]> Execute(BattleSession session)
  {
    ArgumentNullException.ThrowIfNull(session);

    if (!Unit.IsAlive)
      return Fail(BattleQueryFailureReason.UnitNotAlive, $"Unit {Unit.Id} is not alive.");

    Option<BattleBoardState.ValidatedPoint> unitPointOption = session.GetUnitPosition(Unit);
    Option<BattleBoardState.ValidatedPoint> destinationPointOption = session.Board.ValidatePoint(Destination);

    if (unitPointOption.IsNone)
      return Fail(BattleQueryFailureReason.InvalidTile, $"Unit {Unit.Id} is not on the board.");
    if (destinationPointOption.IsNone)
      return Fail(BattleQueryFailureReason.InvalidTile, $"Destination {Destination} is outside the battle board.");

    BattleBoardState.ValidatedPoint unitPoint = unitPointOption.IfNone(default(BattleBoardState.ValidatedPoint));
    BattleBoardState.ValidatedPoint destinationPoint = destinationPointOption.IfNone(default(BattleBoardState.ValidatedPoint));

    return Succeed(session.Board.FindPath(unitPoint, destinationPoint, Unit.Id));
  }
}

public sealed class GetPossibleMoveTilesForUnit : BattleSessionQuery<IReadOnlyCollection<BattleBoardState.ValidatedPoint>>
{
  public const string Id = "get_possible_move_tiles_for_unit";

  public BattleUnitState Unit { get; }
  public int ActionPointCostPerStep { get; }

  public GetPossibleMoveTilesForUnit(
    BattleUnitState unit,
    int actionPointCostPerStep = BattleSession.DefaultMovementStepActionPointCost)
    : base(Id)
  {
    ArgumentNullException.ThrowIfNull(unit);
    Unit = unit;
    if (actionPointCostPerStep < 0)
      throw new ArgumentOutOfRangeException(nameof(actionPointCostPerStep), "Action point cost cannot be negative.");

    ActionPointCostPerStep = actionPointCostPerStep;
  }

  internal override Either<BattleQueryFailure, IReadOnlyCollection<BattleBoardState.ValidatedPoint>> Execute(BattleSession session)
  {
    ArgumentNullException.ThrowIfNull(session);

    if (!Unit.IsAlive)
      return Fail(BattleQueryFailureReason.UnitNotAlive, $"Unit {Unit.Id} is not alive.");
    if (Unit.CurrentActionPoints < ActionPointCostPerStep)
      return Succeed([]);

    Option<BattleBoardState.ValidatedPoint> unitPointOption = session.GetUnitPosition(Unit);
    if (unitPointOption.IsNone)
      return Fail(BattleQueryFailureReason.InvalidTile, $"Unit {Unit.Id} is not on the board.");

    BattleBoardState.ValidatedPoint unitPoint = unitPointOption.IfNone(default(BattleBoardState.ValidatedPoint));

    List<BattleBoardState.ValidatedPoint> possibleTiles = [];
    foreach (BattleBoardState.ValidatedPoint candidatePoint in EnumerateCandidatePoints(session.Board, Unit, unitPoint))
    {
      if (candidatePoint == unitPoint)
        continue;
      if (!session.Board.CanOccupy(candidatePoint))
        continue;

      BattleBoardState.ValidatedPoint[] path = session.Board.FindPath(unitPoint, candidatePoint, Unit.Id);
      if (path.Length == 0)
        continue;

      int stepCount = path.Length - 1;
      if (CanPayMovementCost(Unit, stepCount))
        possibleTiles.Add(candidatePoint);
    }

    return Succeed(possibleTiles);
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
