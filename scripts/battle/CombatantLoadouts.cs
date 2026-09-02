using FunProject.Combatants;

namespace FunProject.Battle;

/// <summary>
/// The strategic loadout bridge: maps a campaign combatant's typed equipment slots onto
/// the battle loadout 1:1 — the proof types line up on both sides, no conversion.
/// Feeds BattleFactory.Start's playerRosterOverride seam.
/// </summary>
public static class CombatantLoadouts
{
  public static UnitLoadout ToBattleLoadout(this Combatant combatant)
    => new(combatant, combatant.EquippedWeapon, combatant.EquippedArmor);
}
