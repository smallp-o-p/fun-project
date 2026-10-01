using FunProject.Combatants;
using System;
using System.Collections.Generic;

namespace FunProject.Battle;

public sealed class SurviveUntilTurnObjective : Objective
{
  private readonly int _targetTurn;

  public SurviveUntilTurnObjective(SurviveUntilTurnObjectiveData data) : base(data)
  {
    _targetTurn = data.TargetTurn;
  }

  public override IReadOnlyCollection<Type> ObservedEventKeys { get; } = [typeof(TurnStartedBattleEvent)];

  public override ObjectiveResult Check(Faction _, BattleEvent battleEvent, BattleReadContext context) =>
    context.CurrentTurn.Match(turn => turn.RoundNumber >= _targetTurn, () => false)
      ? ObjectiveResult.Passed
      : ObjectiveResult.Ongoing;
}
