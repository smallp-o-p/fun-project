using Godot;

namespace FunProject.Stats;

[GlobalClass]
public partial class CriticalChanceStat : Stat
{
  public override StatType StatType => StatType.CriticalChance;
}
