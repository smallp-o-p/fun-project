using System;

namespace FunProject.Battle;

public sealed class SurviveUntilTurnObjective : Objective
{
  private readonly SurviveUntilTurnObjectiveData _data;

  public SurviveUntilTurnObjective(SurviveUntilTurnObjectiveData data)
  {
    ArgumentNullException.ThrowIfNull(data);
    _data = data;
  }

  public override ObjectiveData Data => _data;

  public override bool IsComplete(BattleSession session) => session.TurnNumber >= _data.TargetTurn;
  public override bool IsFailed(BattleSession session) => !session.HasLivingUnits(Owner);
}
