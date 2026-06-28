using Godot;
using System;
using System.Collections.Generic;

namespace FunProject.Battle;

public sealed class FindPathForUnit : BattleSessionQuery<BattleBoardState.ValidatedPoint[]>
{
  public BattleUnitState Unit { get; }
  public Vector3I Destination { get; }

  public FindPathForUnit(BattleUnitState unit, Vector3I destination)
  {
    ArgumentNullException.ThrowIfNull(unit);
    Unit = unit;
    Destination = destination;
  }

  internal override Either<BattleQueryFailure, BattleBoardState.ValidatedPoint[]> Execute(BattleSession session)
  {
    if (!Unit.IsAlive)
      return FailUnitNotAlive(Unit);

    return RequirePosition(session, Unit).Bind(unitPoint =>
      session.Board.ValidatePoint(Destination).Match(
        Some: destinationPoint => Succeed(session.Board.FindPath(unitPoint, destinationPoint, Unit.Id)),
        None: () => Fail(BattleQueryFailureReason.InvalidTile, $"Destination {Destination} is outside the battle board.")));
  }
}

public sealed class GetPossibleMoveTilesForUnit : BattleSessionQuery<IReadOnlyCollection<BattleBoardState.ValidatedPoint>>
{
  public BattleUnitState Unit { get; }
  public int ActionPointCostPerStep { get; }

  public GetPossibleMoveTilesForUnit(
    BattleUnitState unit,
    int actionPointCostPerStep = BattleSession.DefaultMovementStepActionPointCost)
  {
    ArgumentNullException.ThrowIfNull(unit);
    Unit = unit;
    if (actionPointCostPerStep < 0)
      throw new ArgumentOutOfRangeException(nameof(actionPointCostPerStep), "Action point cost cannot be negative.");

    ActionPointCostPerStep = actionPointCostPerStep;
  }

  internal override Either<BattleQueryFailure, IReadOnlyCollection<BattleBoardState.ValidatedPoint>> Execute(BattleSession session)
  {
    if (!Unit.IsAlive)
      return FailUnitNotAlive(Unit);
    if (Unit.CurrentActionPoints < ActionPointCostPerStep)
      return Succeed([]);

    // Zero step cost means an unlimited budget; the visited set still bounds the fill to the board.
    int maxSteps = ActionPointCostPerStep == 0
      ? int.MaxValue
      : Unit.CurrentActionPoints / ActionPointCostPerStep;
    return RequirePosition(session, Unit).Bind(unitPoint =>
      Succeed(session.Board.GetReachableTiles(unitPoint, maxSteps)));
  }
}
