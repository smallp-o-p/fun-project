using System;
using System.Collections.Generic;
using FunProject.Buffs;
using FunProject.Stats;

namespace FunProject.Battle;

// Narrow host preparation: coefficients and membership, never a decision snapshot.
internal sealed class BattleStatInputs
{
  private readonly Dictionary<Type, float> _bases;
  private readonly Dictionary<Type, List<StatModifierValue>> _fixed;
  private readonly Dictionary<Buff, Dictionary<Type, List<StatModifierValue>>> _buffs;

  private BattleStatInputs(IReadOnlyDictionary<Type, float> bases,
    Dictionary<Type, List<StatModifierValue>> fixedModifiers,
    Dictionary<Buff, Dictionary<Type, List<StatModifierValue>>> buffs)
  { _bases = new(bases); _fixed = fixedModifiers; _buffs = buffs; }

  internal static BattleStatInputs Capture(IReadOnlyDictionary<Type, float> baseValues,
    IEnumerable<StatMod> fixedSources, IReadOnlyList<Buff> grantedBuffs)
  {
    var buffs = new Dictionary<Buff, Dictionary<Type, List<StatModifierValue>>>(ReferenceEqualityComparer.Instance);
    foreach (var buff in grantedBuffs)
      if (!buffs.ContainsKey(buff)) buffs.Add(buff, CaptureModifiers(buff.StatMods));
    return new(baseValues, CaptureModifiers(fixedSources), buffs);
  }

  private static Dictionary<Type, List<StatModifierValue>> CaptureModifiers(IEnumerable<StatMod> sources)
  {
    Dictionary<Type, List<StatModifierValue>> values = [];
    foreach (var source in sources)
    {
      ArgumentNullException.ThrowIfNull(source);
      if (!values.TryGetValue(source.TargetType, out var target)) values.Add(source.TargetType, target = []);
      foreach (var modifier in source.Modifiers) target.Add(new(modifier.Operation, modifier.Value));
    }
    return values;
  }

  internal Option<float> TryResolve<TStat>(IEnumerable<Buff> activeBuffs) where TStat : Stat
  {
    if (!_bases.TryGetValue(typeof(TStat), out float baseValue)) return None;
    List<StatModifierValue> modifiers = [];
    if (_fixed.TryGetValue(typeof(TStat), out var fixedModifiers)) modifiers.AddRange(fixedModifiers);
    // Repeated grants remain repeated, in the same order as the resource fold.
    foreach (var buff in activeBuffs)
      if (_buffs.TryGetValue(buff, out var values) && values.TryGetValue(typeof(TStat), out var active))
        modifiers.AddRange(active);
    return Some(HasStats.Fold(baseValue, modifiers));
  }
}
