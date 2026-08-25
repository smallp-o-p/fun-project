using Godot;

namespace FunProject.Battle;

[GlobalClass]
public sealed partial class DefuseAllBombsObjectiveData : ObjectiveData
{
  public override Objective Instantiate() => new DefuseAllBombsObjective(this);
}
