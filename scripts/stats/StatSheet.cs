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
    _stats = stats;
  }

  public Option<Stat> TryGetStat(Type statType)
  {
    return _stats.TryGetValue(statType, out var stat)
      ? Some(stat)
      : None;
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

  internal void Set<TStat>(TStat stat) where TStat : Stat
  {
    _stats[typeof(TStat)] = stat;
  }
}
