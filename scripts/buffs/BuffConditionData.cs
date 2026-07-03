using Godot;

namespace FunProject.Buffs;

/// <summary>
/// Authored activation condition for a buff. Dumb data: identity is the concrete subclass
/// (no enums); evaluation lives battle-side in BuffCondition, mapped by BuffCondition.Create.
/// </summary>
[GlobalClass]
public abstract partial class BuffConditionData : Resource
{
}

/// <summary>True while the unit's current health is strictly below Percent% of its max health.</summary>
[GlobalClass]
public partial class HealthBelowPercentConditionData : BuffConditionData
{
  [Export(PropertyHint.Range, "0,100")] public float Percent { get; set; } = 50f;
}

/// <summary>True while a living enemy unit occupies an orthogonally adjacent tile.</summary>
[GlobalClass]
public partial class AdjacentEnemyConditionData : BuffConditionData
{
}
