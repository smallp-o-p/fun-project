using Godot;

namespace FunProject.Stats;

public enum ModifierOperation
{
  Add,
  Multiply,
  CapMin,
  CapMax,
  PercentAdd,
  Override,
}

[GlobalClass]
public partial class StatModifier : Resource
{
  [Export] public ModifierOperation Operation { get; set; }
  [Export] public float Value { get; set; } = 0f;

  public static StatModifier Add(float value) => new StatModifier { Operation = ModifierOperation.Add, Value = value };
  public static StatModifier Multiply(float multiplier) => new StatModifier { Operation = ModifierOperation.Multiply, Value = multiplier };
  public static StatModifier CapMin(float min) => new StatModifier { Operation = ModifierOperation.CapMin, Value = min };
  public static StatModifier CapMax(float max) => new StatModifier { Operation = ModifierOperation.CapMax, Value = max };
  public static StatModifier PercentAdd(float fraction) => new StatModifier { Operation = ModifierOperation.PercentAdd, Value = fraction };
  public static StatModifier Override(float value) => new StatModifier { Operation = ModifierOperation.Override, Value = value };
}
