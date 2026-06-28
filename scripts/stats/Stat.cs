using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace FunProject.Stats;

public abstract partial class Stat : Resource
{
  [Export] public int BaseValue { get; set; } = 0;
}

public interface HasStats
{
  Option<TStat> TryGetStat<TStat>() where TStat : Stat;
  TStat GetStat<TStat>() where TStat : Stat;

  /// <summary>
  /// Effective value of <typeparamref name="TStat"/>: this owner's BaseValue with every
  /// <see cref="StatMod"/> in <paramref name="sources"/> that targets it folded in.
  /// Returns 0 when this owner has no <typeparamref name="TStat"/>.
  /// </summary>
  float Resolve<TStat>(IEnumerable<StatMod> sources) where TStat : Stat
    => TryResolve<TStat>(sources).IfNone(0f);

  /// <summary>
  /// Like <see cref="Resolve{TStat}"/> but None when this owner has no <typeparamref name="TStat"/>.
  /// </summary>
  Option<float> TryResolve<TStat>(IEnumerable<StatMod> sources) where TStat : Stat
    => TryGetStat<TStat>().Map(stat => Fold(
         stat.BaseValue,
         sources.Where(mod => mod.TargetType == typeof(TStat)).SelectMany(mod => mod.Modifiers)));

  /// <summary>
  /// Deterministic, source-order-independent fold of <see cref="StatModifier"/> deltas onto a base
  /// value: Override ?? (base + ΣAdd) * (1 + ΣPercentAdd) * ΠMultiply, then CapMin/CapMax as a final
  /// clamp. Pure arithmetic — independent of any stat owner.
  /// </summary>
  public static float Fold(float baseValue, IEnumerable<StatModifier> modifiers)
  {
    float flat = 0f;
    float percentAdd = 0f;
    float multiply = 1f;
    float? min = null;
    float? max = null;
    float? over = null;

    foreach (StatModifier modifier in modifiers)
    {
      switch (modifier.Operation)
      {
        case ModifierOperation.Add: flat += modifier.Value; break;
        case ModifierOperation.PercentAdd: percentAdd += modifier.Value; break;
        case ModifierOperation.Multiply: multiply *= modifier.Value; break;
        case ModifierOperation.Override: over = modifier.Value; break;
        case ModifierOperation.CapMin: min = min is null ? modifier.Value : Mathf.Max(min.Value, modifier.Value); break;
        case ModifierOperation.CapMax: max = max is null ? modifier.Value : Mathf.Min(max.Value, modifier.Value); break;
      }
    }

    float value = over ?? (baseValue + flat) * (1f + percentAdd) * multiply;
    if (min is not null) value = Mathf.Max(value, min.Value);
    if (max is not null) value = Mathf.Min(value, max.Value);
    // Round to 5 dp to suppress IEEE-754 noise (e.g. 1.2f*1.2f*10 = 14.400001f); game stats need no finer precision.
    return MathF.Round(value, 5);
  }
}
