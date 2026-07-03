using Godot;
using System;
using System.Collections.Generic;

namespace FunProject.Battle;

public sealed class FindPathForUnit : BattleSessionQuery<BattleBoardState.ValidatedPoint[]>
{
  public AliveUnit Unit { get; }
  public BattleBoardState.ValidatedPoint Destination { get; }

  public FindPathForUnit(AliveUnit unit, BattleBoardState.ValidatedPoint destination)
  {
    Unit = unit;
    Destination = destination;
  }

  internal override Either<BattleQueryFailure, BattleBoardState.ValidatedPoint[]> Execute(BattleSession session)
  {
    return Succeed(session.Board.FindPath(Unit.Position, Destination, Unit.Id));
  }
}

public sealed class GetPossibleMoveTilesForUnit : BattleSessionQuery<IReadOnlyCollection<BattleBoardState.ValidatedPoint>>
{
  public AliveUnit Unit { get; }
  public int ActionPointCostPerStep { get; }

  public GetPossibleMoveTilesForUnit(
    AliveUnit unit,
    int actionPointCostPerStep = BattleSession.DefaultMovementStepActionPointCost)
  {
    Unit = unit;
    if (actionPointCostPerStep < 0)
      throw new ArgumentOutOfRangeException(nameof(actionPointCostPerStep), "Action point cost cannot be negative.");

    ActionPointCostPerStep = actionPointCostPerStep;
  }

  internal override Either<BattleQueryFailure, IReadOnlyCollection<BattleBoardState.ValidatedPoint>> Execute(BattleSession session)
  {
    if (Unit.State.CurrentActionPoints < ActionPointCostPerStep)
      return Succeed([]);

    // Zero step cost means an unlimited budget; the visited set still bounds the fill to the board.
    int maxSteps = ActionPointCostPerStep == 0
      ? int.MaxValue
      : Unit.State.CurrentActionPoints / ActionPointCostPerStep;
    return Succeed(session.Board.GetReachableTiles(Unit.Position, maxSteps));
  }
}
