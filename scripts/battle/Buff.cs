using System;
using FunProject.Buffs;

namespace FunProject.Battle;

/// <summary>
/// Mutable per-unit instance of an authored buff. Identity is the BuffData resource itself
/// (a buff granted by several sources dedupes to one instance). Lifecycle is a pure
/// condition mirror: IsActive tracks the condition's result as of the last evaluation —
/// no durations, no stacking.
/// </summary>
public sealed class Buff
{
  public BuffData Data { get; }
  public bool IsActive { get; private set; }

  private readonly BuffCondition _condition;

  internal Buff(BuffData data)
  {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Condition is null)
      throw new InvalidOperationException($"Buff '{data.Name}' has no activation condition.");

    Data = data;
    _condition = BuffCondition.Create(data.Condition);
  }

  /// <summary>Re-evaluates the condition and mirrors it into IsActive. True iff the state flipped.</summary>
  internal bool Evaluate(BattleSession session, BattleUnitState unit)
  {
    bool met = _condition.IsMet(session, unit);
    if (met == IsActive)
      return false;

    IsActive = met;
    return true;
  }
}
