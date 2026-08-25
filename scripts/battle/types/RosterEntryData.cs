using FunProject.Combatants;
using FunProject.Items;
using FunProject.Weapons;
using Godot;

namespace FunProject.Battle;

/// <summary>Authored roster entry for a combatant template, quantity, and optional gear.</summary>
[GlobalClass]
public partial class RosterEntryData : Resource
{
  [Export] public required CombatantData Combatant { get; set; }

  [Export] public int Quantity { get; set; } = 1;

  [Export] public WeaponData? Weapon { get; set; }

  [Export] public EquippableItemData? Armor { get; set; }
}
