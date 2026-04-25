using Godot;
using System;
using System.Collections.Generic;

namespace FunProject.Battle;

public sealed class FindPathForUnit : BattleSessionQuery<Vector3I[]>
{
  public const string Id = "find_path_for_unit";

  public int UnitId { get; }
  public Vector3I Destination { get; }

  public FindPathForUnit(int unitId, Vector3I destination)
    : base(Id)
  {
    UnitId = unitId;
    Destination = destination;
  }

  internal override BattleQueryResult<Vector3I[]> Execute(BattleSession session)
  {
    ArgumentNullException.ThrowIfNull(session);

    BattleQueryResult<BattleUnitState> unitResult = new GetLivingUnit(UnitId).Execute(session);
    if (unitResult is BattleQueryFailureResult<BattleUnitState> failureResult)
      return Fail(failureResult.Failure.Reason, failureResult.Failure.Message);

    BattleUnitState unit = ((BattleQuerySuccess<BattleUnitState>)unitResult).Value;
    if (!session.Board.IsInBounds(unit.Position))
      return Fail(BattleQueryFailureReason.InvalidTile, $"Unit {UnitId} is on invalid tile {unit.Position}.");
    if (!session.Board.IsInBounds(Destination))
      return Fail(BattleQueryFailureReason.InvalidTile, $"Destination {Destination} is outside the battle board.");

    return Succeed(session.Board.FindPath(unit.Position, Destination, unit.UnitId));
  }
}

public sealed class GetPossibleMoveTilesForUnit : BattleSessionQuery<IReadOnlyCollection<Vector3I>>
{
  public const string Id = "get_possible_move_tiles_for_unit";

  public int UnitId { get; }
  public int ActionPointCostPerStep { get; }

  public GetPossibleMoveTilesForUnit(
    int unitId,
    int actionPointCostPerStep = BattleSession.DefaultMovementStepActionPointCost)
    : base(Id)
  {
    if (actionPointCostPerStep < 0)
      throw new ArgumentOutOfRangeException(nameof(actionPointCostPerStep), "Action point cost cannot be negative.");

    UnitId = unitId;
    ActionPointCostPerStep = actionPointCostPerStep;
  }

  internal override BattleQueryResult<IReadOnlyCollection<Vector3I>> Execute(BattleSession session)
  {
    ArgumentNullException.ThrowIfNull(session);

    BattleQueryResult<BattleUnitState> unitResult = new GetLivingUnit(UnitId).Execute(session);
    if (unitResult is BattleQueryFailureResult<BattleUnitState> failureResult)
      return Fail(failureResult.Failure.Reason, failureResult.Failure.Message);

    BattleUnitState unit = ((BattleQuerySuccess<BattleUnitState>)unitResult).Value;

    if (unit.CurrentActionPoints < ActionPointCostPerStep)
      return Succeed([]);

    List<Vector3I> possibleTiles = [];
    foreach (Vector3I coordinates in EnumerateCandidateCoordinates(session.Board, unit))
    {
      if (coordinates == unit.Position)
        continue;
      if (!session.Board.CanOccupy(coordinates))
        continue;

      Vector3I[] path = session.Board.FindPath(unit.Position, coordinates, unit.UnitId);

      int stepCount = path.Length - 1;
      if (CanPayMovementCost(unit, stepCount))
        possibleTiles.Add(coordinates);
    }

    return Succeed(possibleTiles.ToArray());
  }

  private IEnumerable<Vector3I> EnumerateCandidateCoordinates(BattleBoardState board, BattleUnitState unit)
  {
    if (ActionPointCostPerStep == 0)
      return board.EnumerateBoardCoordinates();

    int maxSteps = unit.CurrentActionPoints / ActionPointCostPerStep;
    return EnumerateCoordinatesWithinStepBound(board, unit.Position, maxSteps);
  }

  private static IEnumerable<Vector3I> EnumerateCoordinatesWithinStepBound(
    BattleBoardState board,
    Vector3I origin,
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
          if (BattleSession.GetGridDistance(origin, coordinates) <= maxSteps)
            yield return coordinates;
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
