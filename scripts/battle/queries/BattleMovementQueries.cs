using System;
using System.Collections.Generic;

namespace FunProject.Battle;

public sealed class FindPathForUnit(AliveUnit unit, BattleBoardState.ValidatedPoint destination) : IBattleSessionQuery<BattleBoardState.ValidatedPoint[]>
{
  public BattleBoardState.ValidatedPoint[] Execute(BattleSession session)
  {
    return session.Board.FindPath(unit.Position, destination);
  }
}

public sealed class GetPossibleMoveTilesForUnit(AliveUnit unit, int actionPointCostPerStep = BattleSession.DefaultMovementStepActionPointCost) : IBattleSessionQuery<IReadOnlyCollection<BattleBoardState.ValidatedPoint>>
{
  public IReadOnlyCollection<BattleBoardState.ValidatedPoint> Execute(BattleSession session)
  {
    ArgumentOutOfRangeException.ThrowIfLessThan(actionPointCostPerStep, 0);

    if (unit.State.IsIncapacitated || unit.State.CurrentActionPoints < actionPointCostPerStep)
      return [];

    int maxSteps = actionPointCostPerStep == 0
      ? int.MaxValue
      : unit.State.CurrentActionPoints / actionPointCostPerStep;

    return session.Board.GetReachableTiles(unit.Position, maxSteps);
  }
}
