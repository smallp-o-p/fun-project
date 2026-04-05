using System;
using System.Collections.Generic;
using Godot;
using Godot.Collections;

namespace FunProject.Stats;

[GlobalClass]
public partial class EquippableStatMod : Resource
{
  [Export] public string Name { get; set; } = "Mod";
  [Export] public string Description { get; set; } = "";
  [Export] public StatType TargetStat { get; set; }

  [Export] public Array<StatModifier> Modifiers { get; set; } = [];

  public void AddModifier(StatModifier modifier) => Modifiers.Add(modifier);

  public bool RemoveModifier(StatModifier modifier) => Modifiers.Remove(modifier);

  public void ClearModifiers() => Modifiers.Clear();

  public float Apply(HasStats statStick)
  {
    try
    {
      Stat s = statStick.GetStats()[TargetStat];
      float value = s.BaseValue;
      foreach (var m in Modifiers)
        value = m.Apply(value);

      return value;
    }
    catch (KeyNotFoundException)
    {
      // Probably should build barriers to ensure that this doesn't leak in release.
      throw new InvalidOperationException();
    }
  }
}
