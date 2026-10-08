using FunProject.Combatants;
using FunProject.Items;
using FunProject.Items.Capabilities;
using FunProject.Weapons;
using Godot;

namespace FunProject.Battle;

/// <summary>Authored equipment for one spawned unit: a combatant mold plus optional weapon and armor.</summary>
[Tool, GlobalClass]
public partial class UnitLoadoutData : Resource
{
  [Export] public required CombatantData Combatant { get; set; }

  [Export] public WeaponData? Weapon { get; set; }

  private EquippableItemData? _armor;

  [Export]
  public EquippableItemData? Armor
  {
    get => _armor;
    set => _armor = value is not null
      && value.Capabilities.AsValueEnumerable().Any(capability => capability is ArmorCapabilityData)
        ? value : null;
  }

  /// <summary>Fresh combatant/weapon/armor instances owned by the faction; missing gear maps to
  /// None.</summary>
  public UnitLoadout CreateRuntime(Faction faction)
  {
    Option<Weapon> weapon = Weapon is null ? None : Some(ItemRuntimeFactory.CreateWeapon(Weapon));
    Option<ItemWith<ArmorCapability>> armor = Armor is null
      ? None
      : ItemRuntimeFactory.Create(Armor).With<ArmorCapability>();
    return new UnitLoadout(new Combatant(Combatant, faction), weapon, armor);
  }

}
