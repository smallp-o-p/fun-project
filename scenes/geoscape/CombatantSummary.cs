using FunProject.Combatants;
using FunProject.Items;
using FunProject.Stats;
using Godot;

namespace FunProject.Geoscape;

// Shared stat/equipment summary text for the soldier screens (captivity inspection, squad cards).
public static class CombatantSummary
{
  // Contributions are the caller's campaign composition (GameState.CampaignStatContributions);
  // this shared text never gathers them itself.
  public static string StatsText(Combatant unit, string healthLabel,
    SysColGeneric.IEnumerable<StatMod> contributions)
  {
    return string.Join("  ",
      StatText<HealthStat>(unit, healthLabel, contributions),
      StatText<ActionPointsStat>(unit, "Action Points", contributions),
      StatText<WillStat>(unit, "Will", contributions),
      StatText<MovementStat>(unit, "Movement", contributions),
      StatText<VisionStat>(unit, "Vision", contributions),
      StatText<AimStat>(unit, "Aim", contributions));
  }

  private static string StatText<TStat>(Combatant unit, string label,
    SysColGeneric.IEnumerable<StatMod> contributions) where TStat : Stat
    => $"{label}: {Mathf.RoundToInt(unit.Resolve<TStat>(contributions))}";

  public static string EquipmentText(Combatant unit, bool includeMods)
  {
    SysColGeneric.List<string> lines =
    [
      unit.EquippedWeapon.Match(
        weapon => $"Weapon: {weapon.ItemName}", () => "Weapon: — empty —"),
      unit.EquippedArmor.Match(
        armor => $"Armor: {armor.Item.ItemName}", () => "Armor: — empty —"),
    ];

    for (int i = 0; i < unit.MaxInventorySize; i++)
      lines.Add(unit.Inventory.TryGetValue(i, out EquippableItem? item)
        ? $"Utility {i + 1}: {item.ItemName}"
        : $"Utility {i + 1}: — empty —");

    if (includeMods)
    {
      lines.Add(ModsText("Personal mods", unit.GetModSlots()));
      unit.EquippedWeapon.IfSome(weapon => lines.Add(ModsText("Weapon mods", weapon.GetModSlots())));
    }

    return string.Join("\n", lines);
  }

  private static string ModsText(string label, Godot.Collections.Array<ModSlot> slots)
  {
    SysColGeneric.List<string> names = [];
    foreach (ModSlot slot in slots)
      slot.EquippedMod.IfSome(mod => names.Add(mod.Name));
    return names.Count > 0 ? $"{label}: {string.Join(", ", names)}" : $"{label}: — empty —";
  }
}
