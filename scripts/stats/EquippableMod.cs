using System;
using System.Collections.Generic;
using Godot;

namespace FunProject.Stats;

public abstract partial class EquippableMod : Resource
{
  [Export] public string Name { get; set; } = "";
  [Export] public string Description { get; set; } = "";

  public abstract Dictionary<Type, float> Apply(HasStats statStick);

  public Option<float> TryGetAppliedStat<TStat>(HasStats statStick)
    where TStat : Stat
  {
    if (Apply(statStick).TryGetValue(typeof(TStat), out var value))
      return Some(value);

    return None;
  }

  public float GetAppliedStat<TStat>(HasStats statStick)
    where TStat : Stat
  {
    return TryGetAppliedStat<TStat>(statStick).Match(
      value => value,
      () => throw new InvalidOperationException());
  }
}
