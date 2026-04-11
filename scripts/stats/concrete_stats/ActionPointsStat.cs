using Godot;

namespace FunProject.Stats;

[GlobalClass]
public partial class ActionPointsStat : Stat
{
  public override StatType StatType => StatType.ActionPoints;
}
