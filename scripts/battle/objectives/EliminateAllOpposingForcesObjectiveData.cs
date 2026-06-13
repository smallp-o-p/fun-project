using Godot;
using System.Linq;

namespace FunProject.Battle;

[GlobalClass]
public partial class EliminateAllOpposingForcesObjectiveData : ObjectiveData
{
  public override bool IsComplete(Objective objective, BattleSession session)
    => session.GlobalFactionTurnOrder.All(side => side == objective.Owner || !session.HasLivingUnits(side));

  public override bool IsFailed(Objective objective, BattleSession session)
    => !session.HasLivingUnits(objective.Owner);
}
