using System.Collections.Generic;

namespace FunProject.Battle;

/// <summary>
/// Buff condition-mirror (no durations): re-evaluates every buff's activation condition and
/// mirrors the result into Buff.IsActive. Both hooks fire in the Before phase, so buff state
/// lands before TurnStarted/UnitAdded broadcast — and before the AP refresh that follows the
/// turn-start dispatch reads MaxActionPoints. Every flip clamps current health to the
/// (possibly changed) max — the clamp only ever lowers, floors at 1; buffs cannot kill.
/// </summary>
internal sealed class TurnStartBuffHook : BattleHook<TurnStartedBattleEvent>
{
  protected override IReadOnlyList<BattleAction> OnEvent(HookContext context, TurnStartedBattleEvent evt)
  {
    // No snapshot of AliveUnits: conditions are read-only over session state and a flip
    // cannot kill (ClampCurrentHealthToMax floors at 1), so the set cannot change mid-pass.
    foreach (BattleUnitState unit in context.Session.AliveUnits)
      BuffEvaluation.EvaluateUnit(context.Session, unit);
    return [];
  }
}

internal sealed class UnitSpawnedBuffHook : BattleHook<UnitAddedBattleEvent>
{
  protected override IReadOnlyList<BattleAction> OnEvent(HookContext context, UnitAddedBattleEvent evt)
  {
    BuffEvaluation.EvaluateUnit(context.Session, evt.Unit);
    return [];
  }
}

internal static class BuffEvaluation
{
  internal static void EvaluateUnit(BattleSession session, BattleUnitState unit)
  {
    foreach (Buff buff in unit.Buffs)
    {
      if (!buff.Evaluate(session, unit))
        continue;

      unit.ClampCurrentHealthToMax();
      session.RaiseEvents(buff.IsActive
        ? new UnitBuffActivatedBattleEvent(unit, buff.Data)
        : new UnitBuffDeactivatedBattleEvent(unit, buff.Data));
    }
  }
}
