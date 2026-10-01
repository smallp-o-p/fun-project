using System.Collections.Generic;

namespace FunProject.Battle;

/// <summary>Recovers conscious units' stun at their faction's turn end, after status and armor upkeep.</summary>
public sealed class StunRecoverySystem : BattleHook<TurnEndedBattleEvent>
{
  private const uint RecoveryPerTurn = 5;

  protected override IReadOnlyList<BattleAction> OnEvent(HookContext context, TurnEndedBattleEvent evt)
  {
    foreach (var unit in context.Read.State.GetFactionAliveUnits(evt.Faction))
    {
      // Turn-end upkeep belongs to the current running step: a running receiver is
      // required, but a pending terminal decision inside that step does not cancel the
      // residual recovery of conscious survivors. RecoverStun gates dead/unconscious units.
      if (context.Read.RunningSession.IsNone)
        break;

      uint recovered = unit.RecoverStun(RecoveryPerTurn);
      if (recovered > 0)
        context.Read.State.RaiseEvents(new UnitStunRecoveredBattleEvent(unit, recovered, unit.CurrentStun));
    }
    return [];
  }
}
