using Godot;

namespace FunProject.Stats;

[GlobalClass]
public partial class RangeStat : Stat
{
  public override StatType StatType => StatType.Range;
}
