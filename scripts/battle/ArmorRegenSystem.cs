using FunProject.Items.Capabilities;

namespace FunProject.Battle;

/// <summary>
/// Bookkeeping system: at the end of a faction's turn, ticks armor regen for that
/// faction's living units. The shield-recharge counters live on each ArmorCapability;
/// this system is stateless.
/// </summary>
public sealed class ArmorRegenSystem : BattleEventListener
{
  public override void OnEventCommitted(BattleSession session, BattleEvent battleEvent)
  {
    if (battleEvent is not TurnEndedBattleEvent turnEnded)
      return;

    foreach (BattleUnitState unit in session.GetFactionAliveUnits(turnEnded.Faction))
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
