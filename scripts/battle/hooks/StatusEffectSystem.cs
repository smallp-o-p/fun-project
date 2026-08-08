using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using FunProject.Combatants;

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
    Faction faction = turnEnded.Faction;
    foreach (BattleUnitState unit in context.Session.GetFactionAliveUnits(faction).ToImmutableList()) // snapshot: a lethal tick removes the unit from AliveUnits mid-iteration
    {
      foreach (ActiveStatusEffect active in unit.ActiveStatusEffects.ToList())
      {
        active.TickDown();
        context.Session.RaiseEvents(new UnitStatusEffectTickedBattleEvent(unit, active.Spec, active.RemainingTurns));

        active.OnFactionTurnEnd(context.Session, unit);

        if (unit.IsDead)
          break;

        if (active.IsExpired)
        {
          unit.RemoveStatusEffect(active.Spec);
          context.Session.RaiseEvents(new UnitStatusEffectExpiredBattleEvent(unit, active.Spec));
        }
      }
    }

    return [];
  }
}
