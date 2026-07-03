using System.Collections.Generic;
using System.Linq;

namespace FunProject.Stats;

public static class HasStatsExtensions
{
  /// <summary>
  /// Effective value of <typeparamref name="TStat"/>: the owner's BaseValue with every
  /// <see cref="StatMod"/> in <paramref name="sources"/> that targets it folded in.
  /// Returns 0 when the owner has no <typeparamref name="TStat"/>.
  /// </summary>
  public static float Resolve<TStat>(this HasStats owner, IEnumerable<StatMod> sources) where TStat : Stat
    => owner.TryResolve<TStat>(sources).IfNone(0f);

  /// <summary>
  /// Like <see cref="Resolve{TStat}"/> but None when the owner has no <typeparamref name="TStat"/>.
  /// </summary>
  public static Option<float> TryResolve<TStat>(this HasStats owner, IEnumerable<StatMod> sources) where TStat : Stat
    => owner.TryGetStat<TStat>().Map(stat => HasStats.Fold(
         stat.BaseValue,
         sources.Where(mod => mod.TargetType == typeof(TStat)).SelectMany(mod => mod.Modifiers)));
}
