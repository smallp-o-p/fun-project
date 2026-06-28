using System;
using Godot;
using Godot.Collections;

namespace FunProject.Stats;

public abstract partial class StatMod : Resource
{
  public abstract Type TargetType { get; }

  [Export] public Array<StatModifier> Modifiers { get; set; } = [];

  public void AddModifier(StatModifier modifier) => Modifiers.Add(modifier);

  public bool RemoveModifier(StatModifier modifier) => Modifiers.Remove(modifier);

  public void ClearModifiers() => Modifiers.Clear();
}
