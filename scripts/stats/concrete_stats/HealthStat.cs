using Godot;

namespace FunProject.Stats;

[GlobalClass]
public partial class HealthStat : Stat
{
  public override StatType StatType => StatType.Health;
}
