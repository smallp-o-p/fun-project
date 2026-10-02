using System.Collections.Generic;

namespace FunProject.Battle;

/// <summary>
/// Turn-end hook: at the end of a faction's turn, ticks status effects on that
/// faction's living units. The decrement precedes the damage so a duration-N DoT deals
/// exactly N ticks and the ticked event reports the post-tick remaining turns. DoT damage
/// goes through the normal ApplyDamageTo pipeline (armor split, events, death handling,
/// regen-delay re-arm). State lives on each unit; this pass is stateless. Registered at
/// priority -100 so it runs before <see cref="ArmorRegenSystem"/> (a DoT tick re-arms the
/// regen delay).
/// </summary>
public sealed class StatusEffectSystem : BattleHook<TurnEndedBattleEvent>
{
  protected override IReadOnlyList<BattleAction> OnEvent(HookContext context, TurnEndedBattleEvent turnEnded)
  {
    // Status effects tick through the running receiver: they are synchronous upkeep of the
    // ending turn's step, and completed event contexts carry no receiver by contract.
    if (context.Read.RunningSession.Case is not BattleSession session)
      return [];

    foreach (BattleUnitState unit in session.State.GetFactionAliveUnits(turnEnded.Faction)) // snapshot: a lethal tick removes the unit from AliveUnits mid-iteration
    {
      foreach (ActiveStatusEffect active in unit.ActiveStatusEffects.AsValueEnumerable().ToList())
      {
        active.TickDown();
        session.RaiseEvents(new UnitStatusEffectTickedBattleEvent(unit, active.Spec, active.RemainingTurns));

        active.OnFactionTurnEnd(session, unit);

        if (unit.IsDead)
          break;

        if (active.IsExpired)
        {
          unit.RemoveStatusEffect(active.Spec);
          session.RaiseEvents(new UnitStatusEffectExpiredBattleEvent(unit, active.Spec));
        }
      }
    }

    return [];
  }
}
