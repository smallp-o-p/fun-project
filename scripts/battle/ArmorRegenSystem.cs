using FunProject.Combatants;
using FunProject.Items.Capabilities;
using System.Linq;

namespace FunProject.Battle;

/// <summary>
/// Turn-end bookkeeping listener: at the end of a faction's turn, ticks armor regen for that
/// faction's living units. The shield-recharge counters live on each ArmorCapability; this
/// pass is stateless. Registered High but after <see cref="StatusEffectSystem"/>, so a DoT
/// tick that re-arms the regen delay suppresses this turn's recharge. Mutates state and raises
/// follow-up events only; it never submits executor actions.
/// </summary>
public sealed class ArmorRegenSystem : BattleEventListener<TurnEndedBattleEvent>
{
  protected override void OnEvent(BattleSession session, TurnEndedBattleEvent turnEnded)
  {
    Faction faction = turnEnded.Faction;
    foreach (BattleUnitState unit in session.GetFactionAliveUnits(faction).ToList())
    {
      unit.EquippedArmor.IfSome(armor =>
      {
        ArmorCapability capability = armor.Capability;
        if (!capability.CanRegen)
          return;

        int restored = capability.TickRegen();
        if (restored > 0)
          session.RaiseEvent(new UnitArmorRegeneratedBattleEvent(unit, restored, capability.Current));
      });
    }
  }
}
