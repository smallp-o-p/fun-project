using Godot;

namespace FunProject.Stats;

/// <summary>
/// A single transformation that can be applied to a float value.
/// Create concrete subclasses for each kind of modification you need.
/// </summary>
[GlobalClass]
public partial class StatModifier : Resource
{
  public virtual float Apply(float value) { return value; }
}

[GlobalClass]
public partial class StatModifier_Add : StatModifier
{
  [Export] public float Value { get; set; } = 0f;

  public override float Apply(float value) => value + Value;
}

[GlobalClass]
public partial class StatModifier_Multiply : StatModifier
{
  [Export] public float Multiplier { get; set; } = 1f;

  public override float Apply(float value) => value * Multiplier;
}

[GlobalClass]
public partial class StatModifier_CapMin : StatModifier
{
  [Export] public float Min { get; set; } = 0f;

  public override float Apply(float value) => Mathf.Max(value, Min);
}

[GlobalClass]
public partial class StatModifier_CapMax : StatModifier
{
  [Export] public float Max { get; set; } = 0f;

  public override float Apply(float value) => Mathf.Min(value, Max);
}
