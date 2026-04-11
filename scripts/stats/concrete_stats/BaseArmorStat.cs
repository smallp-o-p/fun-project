using Godot;

namespace FunProject.Stats;

[GlobalClass]
public partial class BaseArmorStat : Stat
{
  public override StatType StatType => StatType.BaseArmor;
}
