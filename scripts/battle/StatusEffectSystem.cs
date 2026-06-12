using System.Linq;
using FunProject.Items.Effects;
using FunProject.Weapons;

namespace FunProject.Battle;

/// <summary>
/// Bookkeeping system: at the end of a faction's turn, ticks status effects on that
/// faction's living units. The decrement precedes the damage so a duration-N DoT deals
/// exactly N ticks and the ticked event reports the post-tick remaining turns. DoT
/// damage goes through the normal ApplyDamageTo pipeline (armor split, events, death
/// handling, regen-delay re-arm). State lives on each unit; this system is stateless.
/// </summary>
public sealed class StatusEffectSystem : BattleEventListener
{
  public override void OnEventCommitted(BattleSession session, BattleEvent battleEvent)
  {
    if (battleEvent is not TurnEndedBattleEvent turnEnded)
      return;

    foreach (BattleUnitState unit in session.GetFactionAliveUnits(turnEnded.Faction).ToList()) // snapshot: a lethal tick removes the unit from AliveUnits mid-iteration
    {
      foreach (ActiveStatusEffect active in unit.ActiveStatusEffects.ToList())
      {
        active.TickDown();
        session.RaiseEvent(new UnitStatusEffectTickedBattleEvent(unit, active.Spec, active.RemainingTurns));

        if (active.Spec is DamageOverTimeStatusSpecData dot && dot.TickDamage > 0)
          session.ApplyDamageTo(unit, [new Damage(dot.TickDamage, dot.TickElement)]);

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
