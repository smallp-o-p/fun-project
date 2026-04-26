using System;
using System.Collections.Generic;
using Godot;

namespace FunProject.Stats;

public abstract partial class EquippableStatMod : EquippableMod
{
  protected abstract Type TargetStatType { get; }

  [Export] public Godot.Collections.Array<StatModifier> Modifiers { get; set; } = [];

  public void AddModifier(StatModifier modifier) => Modifiers.Add(modifier);

  public bool RemoveModifier(StatModifier modifier) => Modifiers.Remove(modifier);

  public void ClearModifiers() => Modifiers.Clear();

  public float ApplyToTarget(HasStats statStick)
  {
    Stat stat = statStick.TryGetStat(TargetStatType).Match(
      foundStat => foundStat,
      () => throw new InvalidOperationException());

    float value = stat.BaseValue;
    foreach (var modifier in Modifiers)
      value = modifier.Apply(value);

    return value;
  }

  public override Dictionary<Type, float> Apply(HasStats statStick) =>
    new()
    {
      [TargetStatType] = ApplyToTarget(statStick)
    };
}
