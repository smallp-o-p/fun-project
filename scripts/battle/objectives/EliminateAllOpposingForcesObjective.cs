using System.Linq;

namespace FunProject.Battle;

public sealed class EliminateAllOpposingForcesObjective : Objective
{
  public EliminateAllOpposingForcesObjective(ObjectiveData data) : base(data)
  {
  }

  public override bool IsComplete(BattleSession session) =>
    session.GlobalFactionTurnOrder.All(side => side == Owner || !session.HasLivingUnits(side));

  public override bool IsFailed(BattleSession session) => !session.HasLivingUnits(Owner);
}
