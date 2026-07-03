using System;
using System.Linq;
using FunProject.Buffs;

namespace FunProject.Battle;

/// <summary>
/// Runtime evaluators for authored buff activation conditions. Mirrors ActiveStatusEffect:
/// Create is the single, localized Data->Runtime switch; identity is the concrete data
/// subclass (no enums). Session-dependent evaluation stays here, out of authored resources.
/// </summary>
public abstract class BuffCondition
{
  internal abstract bool IsMet(BattleSession session, BattleUnitState unit);

  internal static BuffCondition Create(BuffConditionData data)
  {
    ArgumentNullException.ThrowIfNull(data);
    return data switch
    {
      HealthBelowPercentConditionData healthBelow => new HealthBelowPercentCondition(healthBelow),
      AdjacentEnemyConditionData => new AdjacentEnemyCondition(),
      _ => throw new ArgumentException(
        $"No runtime buff condition mapped for data type {data.GetType().Name}.",
        nameof(data)),
    };
  }
}

/// <summary>True while the unit's current health is strictly below Percent% of its max health.</summary>
public sealed class HealthBelowPercentCondition : BuffCondition
{
  private readonly HealthBelowPercentConditionData _data;

  internal HealthBelowPercentCondition(HealthBelowPercentConditionData data)
  {
    _data = data;
  }

  internal override bool IsMet(BattleSession session, BattleUnitState unit)
    => unit.CurrentHealth < unit.MaxHealth * (_data.Percent / 100f);
}

/// <summary>True while any living enemy unit occupies an orthogonally adjacent tile.</summary>
public sealed class AdjacentEnemyCondition : BuffCondition
{
  internal override bool IsMet(BattleSession session, BattleUnitState unit)
    => session.GetUnitPosition(unit).Match(
         position => session.AliveUnits
           .Where(other => other.Side != unit.Side)
           .Any(other => session.GetUnitPosition(other).Match(
             otherPosition => BattleBoardState.AreAdjacent(position, otherPosition),
             () => false)),
         () => false);
}
