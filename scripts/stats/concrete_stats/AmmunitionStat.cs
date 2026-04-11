using Godot;

namespace FunProject.Stats;

[GlobalClass]
public partial class AmmunitionStat : Stat
{
  public override StatType StatType => StatType.Ammunition;
}
