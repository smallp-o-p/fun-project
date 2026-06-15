using System;
using System.Linq;

namespace FunProject.Battle;

public sealed class EliminateAllOpposingForcesObjective : Objective
{
  private readonly EliminateAllOpposingForcesObjectiveData _data;

  public EliminateAllOpposingForcesObjective(EliminateAllOpposingForcesObjectiveData data)
  {
    ArgumentNullException.ThrowIfNull(data);
    _data = data;
  }

  public override ObjectiveData Data => _data;

  public override bool IsComplete(BattleSession session) =>
    session.GlobalFactionTurnOrder.All(side => side == Owner || !session.HasLivingUnits(side));

  public override bool IsFailed(BattleSession session) => !session.HasLivingUnits(Owner);
}
