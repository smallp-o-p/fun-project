using FunProject.Combatants;
using FunProject.Items;
using FunProject.Items.Capabilities;
using FunProject.Weapons;
using Godot;
using System;

namespace FunProject.Battle;

/// <summary>Authored equipment for one spawned unit: a combatant mold plus optional weapon and armor.</summary>
[GlobalClass]
public partial class UnitLoadoutData : Resource
{
  [Export] public required CombatantData Combatant { get; set; }

  [Export] public WeaponData? Weapon { get; set; }

  [Export] public EquippableItemData? Armor { get; set; }

  /// <summary>Authoring guard: required combatant present and armor is armor-capable equipment.</summary>
  public void Validate()
  {
    if (Combatant is null)
      throw new InvalidOperationException($"{nameof(UnitLoadoutData)} requires a {nameof(Combatant)}.");
    if (Armor is not null && !HasArmorCapability(Armor))
      throw new InvalidOperationException(
        $"Loadout armor '{Armor.Name}' carries no {nameof(ArmorCapabilityData)}; only armor-capable items can be equipped as armor.");
  }

  /// <summary>Fresh combatant/weapon/armor instances owned by the faction; missing gear maps to
  /// None and malformed armor throws instead of being silently dropped.</summary>
  public UnitLoadout CreateRuntime(Faction faction)
  {
    Validate();
    Option<Weapon> weapon = Weapon is null ? None : Some(ItemRuntimeFactory.CreateWeapon(Weapon));
    Option<ItemWith<ArmorCapability>> armor = Armor is null
      ? None
      : ItemRuntimeFactory.Create(Armor).With<ArmorCapability>();
    return new UnitLoadout(new Combatant(Combatant, faction), weapon, armor);
  }

  private static bool HasArmorCapability(EquippableItemData item)
  {
    foreach (ItemCapabilityData capability in item.Capabilities)
      if (capability is ArmorCapabilityData)
        return true;
    return false;
  }
}
