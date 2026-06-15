using Godot;

namespace FunProject.Battle;

[GlobalClass]
public partial class SurviveUntilTurnObjectiveData : ObjectiveData
{
  [Export] public int TargetTurn { get; set; }

  public override Objective CreateRuntime() => new SurviveUntilTurnObjective(this);
}
