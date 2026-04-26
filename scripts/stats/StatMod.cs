using System;
using Godot;
using Godot.Collections;

namespace FunProject.Stats;

public abstract partial class StatMod : Resource
{
  protected abstract Type TargetStatType { get; }
  public Type TargetType => TargetStatType;

  [Export] public Array<StatModifier> Modifiers { get; set; } = [];

  public void AddModifier(StatModifier modifier) => Modifiers.Add(modifier);

  public bool RemoveModifier(StatModifier modifier) => Modifiers.Remove(modifier);

  public void ClearModifiers() => Modifiers.Clear();

  public float Apply(HasStats statStick)
  {
    Stat stat = statStick.TryGetStat(TargetStatType).Match(
      foundStat => foundStat,
      () => throw new InvalidOperationException());

    float value = stat.BaseValue;
    foreach (var m in Modifiers)
      value = m.Apply(value);

    return value;
  }
}
