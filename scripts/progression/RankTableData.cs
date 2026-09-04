using Godot;

namespace FunProject.Progression;

/// <summary>
/// Ordered rank ladder; the last entry is the max level. Level N holds entry N's effects
/// plus everything below it — a cumulative ladder, like unlocked skill-path steps.
/// </summary>
[GlobalClass]
public partial class RankTableData : Resource
{
  [Export] public Godot.Collections.Array<RankLevelData> Levels { get; set; } = [];
}
