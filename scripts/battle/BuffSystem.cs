namespace FunProject.Battle;

/// <summary>
/// Turn-start bookkeeping: re-evaluates every buff's activation condition and mirrors the
/// result into Buff.IsActive (condition-mirror lifecycle — no durations). Invoked INLINE by
/// the session at each turn start, BEFORE the acting faction's action-point refresh, so a
/// buff that modifies MaxActionPoints lands before CurrentActionPoints = MaxActionPoints —
/// a TurnStartedBattleEvent listener would run too late (the session raises that event
/// after the refresh). Also invoked per unit at spawn. Every flip clamps current health to
/// the (possibly changed) max — the clamp only ever lowers, activation never heals.
/// Mutates state and raises follow-up events only; it never submits executor actions.
/// </summary>
internal static class BuffSystem
{
  internal static void EvaluateAll(BattleSession session)
  {
    // No snapshot of AliveUnits: conditions are read-only over session state and a flip
    // cannot kill (ClampCurrentHealthToMax floors at 1), so the set cannot change mid-pass.
    foreach (BattleUnitState unit in session.AliveUnits)
      EvaluateUnit(session, unit);
  }

  internal static void EvaluateUnit(BattleSession session, BattleUnitState unit)
  {
    foreach (Buff buff in unit.Buffs)
    {
      if (!buff.Evaluate(session, unit))
        continue;

      unit.ClampCurrentHealthToMax();
      session.RaiseEvent(buff.IsActive
        ? new UnitBuffActivatedBattleEvent(unit, buff.Data)
        : new UnitBuffDeactivatedBattleEvent(unit, buff.Data));
    }
  }
}
