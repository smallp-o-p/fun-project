using Godot;

namespace FunProject.Progression;

/// <summary>One rung of a rank ladder: display name, the XP gain factor applied to gains
/// made while holding this rank, and optional effects granted while held (cumulative — see
/// <see cref="RankTableData"/>).</summary>
[GlobalClass]
public partial class RankLevelData : Resource
{
  [Export] public string Name { get; set; } = "";
  [Export] public int GainFactorPercent { get; set; } = 100;
  [Export] public Godot.Collections.Array<UpgradeEffectData> Effects { get; set; } = [];
}
