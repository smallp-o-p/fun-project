using Godot;

namespace FunProject.Battle;

[GlobalClass]
public sealed partial class SurviveUntilTurnObjectiveData : ObjectiveData
{
  [Export] public int TargetTurn { get; set; } = 1;

  public override Objective Instantiate() => new SurviveUntilTurnObjective(this);
}
