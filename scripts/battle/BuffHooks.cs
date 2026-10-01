using System.Collections.Generic;

namespace FunProject.Battle;

/// <summary>
/// Buff condition-mirror (no durations): re-evaluates every buff's activation condition.
/// Activation flags belong to each unit's grants (BattleUnitState.EvaluateBuffs completes
/// each grant's flag update, health clamp, owner reconciliation, and event before the next
/// grant is evaluated; the owning session reconciles visibility/scheduling BEFORE that
/// grant's event broadcasts). Both hooks are fired by the executor after the
/// TurnStarted/UnitAdded broadcast — and before the AP refresh that follows the turn-start
/// dispatch reads MaxActionPoints. Every flip clamps current health to the (possibly
/// changed) max — the clamp only ever lowers, floors at 1; buffs cannot kill.
/// </summary>
internal sealed class TurnStartBuffHook : BattleHook<TurnStartedBattleEvent>
{
  protected override IReadOnlyList<BattleAction> OnEvent(HookContext context, TurnStartedBattleEvent evt)
  {
    // No snapshot of AliveUnits: conditions are read-only over battle state and a flip
    // cannot kill (ClampCurrentHealthToMax floors at 1), so the set cannot change mid-pass.
    return context.Read.RunningSession.Match(
      Some: session => EvaluateAll(context.Read, session),
      None: () => []);
  }

  private static IReadOnlyList<BattleAction> EvaluateAll(BattleReadContext read, BattleSession session)
  {
    foreach (BattleUnitState unit in read.State.AliveUnits)
      unit.EvaluateBuffs(read, session.ReconcileBuffFlip);
    return [];
  }
}

internal sealed class UnitSpawnedBuffHook : BattleHook<UnitAddedBattleEvent>
{
  protected override IReadOnlyList<BattleAction> OnEvent(HookContext context, UnitAddedBattleEvent evt)
  {
    // Spawn buff evaluation is synchronous bookkeeping of the reinforcement step through
    // the running owner's reconciliation; preparation performs the same evaluation for
    // initial units through its own context.
    context.Read.RunningSession.IfSome(session =>
      evt.Unit.EvaluateBuffs(context.Read, session.ReconcileBuffFlip));
    return [];
  }
}
