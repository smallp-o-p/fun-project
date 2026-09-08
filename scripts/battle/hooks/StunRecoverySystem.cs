using System.Collections.Generic;

namespace FunProject.Battle;

/// <summary>Recovers conscious units' stun at their faction's turn end, after status and armor upkeep.</summary>
public sealed class StunRecoverySystem : BattleHook<TurnEndedBattleEvent>
{
  private const uint RecoveryPerTurn = 5;

  protected override IReadOnlyList<BattleAction> OnEvent(HookContext context, TurnEndedBattleEvent evt)
  {
    foreach (var unit in context.Session.GetFactionAliveUnits(evt.Faction))
    {
      if (context.Session.Phase != BattlePhase.InProgress)
        break;

      uint recovered = unit.RecoverStun(RecoveryPerTurn);
      if (recovered > 0)
        context.Session.RaiseEvents(new UnitStunRecoveredBattleEvent(unit, recovered, unit.CurrentStun));
    }
    return [];
  }
}
