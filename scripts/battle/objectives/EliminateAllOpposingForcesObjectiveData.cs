using Godot;

namespace FunProject.Battle;

[GlobalClass]
public sealed partial class EliminateAllOpposingForcesObjectiveData : ObjectiveData
{
  public override Objective Instantiate() => new EliminateAllOpposingForcesObjective(this);
}
