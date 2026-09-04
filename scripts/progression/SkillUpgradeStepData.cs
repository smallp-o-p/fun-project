using Godot;

namespace FunProject.Progression;

/// <summary>
/// One rung in a path's chain: an authored cost plus the effects granted on unlock.
/// Costs ramp by authoring — later steps simply cost more.
/// </summary>
[GlobalClass]
public partial class SkillUpgradeStepData : Resource
{
  [Export] public int Cost { get; set; } = 1;
  [Export] public Godot.Collections.Array<UpgradeEffectData> Effects { get; set; } = [];
}
