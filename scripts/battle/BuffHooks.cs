using System.Collections.Generic;

namespace FunProject.Battle;

/// <summary>
/// Buff condition-mirror (no durations): re-evaluates every buff's activation condition.
/// Activation flags belong to each unit's grants (BattleUnitState.EvaluateBuffs completes
/// each grant's flag update, health clamp, and event before the next grant is evaluated).
/// Both hooks are fired by the executor after the TurnStarted/UnitAdded broadcast — and
/// before the AP refresh that follows the turn-start dispatch reads MaxActionPoints.
/// Every flip clamps current health to the (possibly changed) max — the clamp only ever
/// lowers, floors at 1; buffs cannot kill.
/// </summary>
internal sealed class TurnStartBuffHook : BattleHook<TurnStartedBattleEvent>
{
  protected override IReadOnlyList<BattleAction> OnEvent(HookContext context, TurnStartedBattleEvent evt)
  {
    // No snapshot of AliveUnits: conditions are read-only over session state and a flip
    // cannot kill (ClampCurrentHealthToMax floors at 1), so the set cannot change mid-pass.
    foreach (BattleUnitState unit in context.Session.AliveUnits)
      unit.EvaluateBuffs(context.Session);
    return [];
  }
}

internal sealed class UnitSpawnedBuffHook : BattleHook<UnitAddedBattleEvent>
{
  protected override IReadOnlyList<BattleAction> OnEvent(HookContext context, UnitAddedBattleEvent evt)
  {
    evt.Unit.EvaluateBuffs(context.Session);
    return [];
  }
}
