using Godot;

namespace FunProject.Stats;

[GlobalClass]
public partial class DamageStat : Stat
{
  public override StatType StatType => StatType.Damage;
}
