using Godot;

namespace FunProject.Battle;

[GlobalClass]
public partial class SurviveUntilTurnObjectiveData : ObjectiveData
{
  [Export] public int TargetTurn { get; set; }

  public override bool IsComplete(Objective objective, BattleSession session)
    => session.TurnNumber >= TargetTurn;

  public override bool IsFailed(Objective objective, BattleSession session)
    => !session.HasLivingUnits(objective.Owner);
}
