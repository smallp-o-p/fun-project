using System;
using System.Collections.Generic;

namespace FunProject.Stats;

/// <summary>
/// Type-keyed stat storage shared by HasStats implementors.
/// </summary>
public sealed class StatSheet
{
  private readonly Dictionary<Type, Stat> _stats;

  public StatSheet(Dictionary<Type, Stat> stats)
  {
    ArgumentNullException.ThrowIfNull(stats);
    foreach (var (statType, stat) in stats)
      if (!statType.IsInstanceOfType(stat))
        throw new ArgumentException(
          $"StatSheet entry for key {statType.Name} holds a {stat?.GetType().Name ?? "null"}.");
    _stats = stats;
  }

  public Option<TStat> TryGetStat<TStat>() where TStat : Stat
  {
    if (_stats.TryGetValue(typeof(TStat), out var foundStat))
    {
      return Some((TStat)foundStat);
    }

    return None;
  }

  public TStat GetStat<TStat>() where TStat : Stat
  {
    return TryGetStat<TStat>().Match(
      stat => stat,
      () => throw new InvalidOperationException());
  }

  internal IReadOnlyDictionary<Type, float> CopyBaseValues()
    => new System.Collections.ObjectModel.ReadOnlyDictionary<Type, float>(
      _stats.AsValueEnumerable().ToDictionary(entry => entry.Key, entry => (float)entry.Value.BaseValue));

  internal void Set<TStat>(TStat stat) where TStat : Stat
  {
    _stats[typeof(TStat)] = stat;
  }
}
