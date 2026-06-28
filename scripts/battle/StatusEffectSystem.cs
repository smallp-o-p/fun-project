using System.Linq;
using FunProject.Combatants;

namespace FunProject.Battle;

/// <summary>
/// Turn-end bookkeeping listener: at the end of a faction's turn, ticks status effects on
/// that faction's living units. The decrement precedes the damage so a duration-N DoT deals
/// exactly N ticks and the ticked event reports the post-tick remaining turns. DoT damage
/// goes through the normal ApplyDamageTo pipeline (armor split, events, death handling,
/// regen-delay re-arm). State lives on each unit; this pass is stateless. Registered High so
/// it runs before <see cref="ArmorRegenSystem"/> (a DoT tick re-arms the regen delay).
/// Mutates state and raises follow-up events only; it never submits executor actions.
/// </summary>
public sealed class StatusEffectSystem : BattleEventListener<TurnEndedBattleEvent>
{
  protected override void OnEvent(BattleSession session, TurnEndedBattleEvent turnEnded)
  {
    Faction faction = turnEnded.Faction;
    foreach (BattleUnitState unit in session.GetFactionAliveUnits(faction).ToList()) // snapshot: a lethal tick removes the unit from AliveUnits mid-iteration
    {
      foreach (ActiveStatusEffect active in unit.ActiveStatusEffects.ToList())
      {
        active.TickDown();
        session.RaiseEvent(new UnitStatusEffectTickedBattleEvent(unit, active.Spec, active.RemainingTurns));

        active.OnFactionTurnEnd(session, unit);

        if (unit.IsDead)
          break;

        if (active.IsExpired)
        {
          unit.RemoveStatusEffect(active.Spec);
          session.RaiseEvent(new UnitStatusEffectExpiredBattleEvent(unit, active.Spec));
        }
      }
    }
  }
}
