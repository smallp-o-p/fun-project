using Godot;

namespace FunProject.Stats;

[GlobalClass]
public partial class MovementStat : Stat
{
  public override StatType StatType => StatType.Movement;
}
