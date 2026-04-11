using System;
using System.Collections.Generic;
using Godot;

namespace FunProject.Stats;

public abstract partial class EquippableMod : Resource
{
  [Export] public string Name { get; set; } = "";
  [Export] public string Description { get; set; } = "";

  public abstract Dictionary<Type, float> Apply(HasStats statStick);

  public bool TryGetAppliedStat<TStat>(HasStats statStick, out float value)
    where TStat : Stat
  {
    if (Apply(statStick).TryGetValue(typeof(TStat), out value))
      return true;

    value = 0f;
    return false;
  }

  public float GetAppliedStat<TStat>(HasStats statStick)
    where TStat : Stat
  {
    if (TryGetAppliedStat<TStat>(statStick, out var value))
      return value;

    throw new InvalidOperationException();
  }
}
