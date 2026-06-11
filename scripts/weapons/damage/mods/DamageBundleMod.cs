using System;
using System.Collections.Generic;
using FunProject.Stats;
using Godot;

namespace FunProject.Weapons;

public abstract partial class DamageBundleMod : Resource
{
  // Bundle in, bundle out — each concrete decides its own packet selection.
  public abstract List<Damage> Apply(List<Damage> bundle, DamageEmissionContext context);

  protected static int FoldOps(int amount, IEnumerable<StatModifier> ops)
  {
    float value = amount;
    foreach (StatModifier op in ops)
    {
      ArgumentNullException.ThrowIfNull(op);
      value = op.Apply(value);
    }

    return Mathf.RoundToInt(value);
  }
}
