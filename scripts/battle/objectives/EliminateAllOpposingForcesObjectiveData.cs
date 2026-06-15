using Godot;

namespace FunProject.Battle;

[GlobalClass]
public partial class EliminateAllOpposingForcesObjectiveData : ObjectiveData
{
  public override Objective CreateRuntime() => new EliminateAllOpposingForcesObjective(this);
}
