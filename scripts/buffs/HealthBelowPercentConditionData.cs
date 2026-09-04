using Godot;

namespace FunProject.Buffs;

/// <summary>True while the unit's current health is strictly below Percent% of its max health.</summary>
[GlobalClass]
public partial class HealthBelowPercentConditionData : BuffConditionData
{
  [Export(PropertyHint.Range, "0,100")] public float Percent { get; set; } = 50f;
}
