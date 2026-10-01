using FunProject.Combatants;
using FunProject.Items.Capabilities;
using System.Collections.Generic;

namespace FunProject.Battle;

/// <summary>
/// Turn-end hook: at the end of a faction's turn, ticks armor regen for that
/// faction's living units. The shield-recharge counters live on each ArmorCapability; this
/// pass is stateless. Registered at priority -100 but after <see cref="StatusEffectSystem"/>,
/// so a DoT tick that re-arms the regen delay suppresses this turn's recharge.
/// </summary>
public sealed class ArmorRegenSystem : BattleHook<TurnEndedBattleEvent>
{
  protected override IReadOnlyList<BattleAction> OnEvent(HookContext context, TurnEndedBattleEvent turnEnded)
  {
    Faction faction = turnEnded.Faction;
    foreach (BattleUnitState unit in context.Read.State.GetFactionAliveUnits(faction).AsValueEnumerable().ToList())
    {
      unit.EquippedArmor.IfSome(armor =>
      {
        ArmorCapability capability = armor.Capability;
        if (!capability.CanRegen)
          return;

        int restored = capability.TickRegen();
        if (restored > 0)
          context.Read.State.RaiseEvents(new UnitArmorRegeneratedBattleEvent(unit, restored, capability.Current));
      });
    }

    return [];
  }
}
