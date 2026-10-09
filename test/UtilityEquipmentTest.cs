using System;
using FunProject.Battle;
using FunProject.Items;
using FunProject.Items.Capabilities;
using FunProject.Weapons;
using Godot;
using GdUnit4;

namespace FunProject.Tests;

[TestSuite]
[RequireGodotRuntime]
public class UtilityEquipmentTest
{
  [TestCase]
  public static void UtilityEquipRequiresMarker()
  {
    using var campaign = new GeoscapeFixture(MakeStart(roster: [MakeEntry()]));
    var unit = campaign.State.Roster[0];
    var kit = new EquippableItem(MakeUtilityData("Field Kit"));
    var utility = kit.With<UtilityCapability>().RequireSome();
    unit.EquipItem(utility, 0);

    EquippableItemData[] rejected = [MakeWeaponData(), MakeArmorData("Vest"), MakeItemData("Workshop Sample")];
    foreach (EquippableItemData data in rejected)
      Assert.True(ItemRuntimeFactory.Create(data).With<UtilityCapability>().IsNone,
        $"'{data.Name}' must not mint a utility equipment proof.");

    Assert.True(ReferenceEquals(kit, unit.Inventory[0].Item));
    Assert.True(ReferenceEquals(kit, unit.UnequipItem(0).RequireSome().Item));
    Assert.Throws<InvalidOperationException>(() => unit.EquipItem(utility, -1));
    Assert.Throws<InvalidOperationException>(() => unit.EquipItem(utility, unit.MaxInventorySize));
    Assert.Equal(0, unit.Inventory.Count);
  }

  [TestCase]
  public static void UtilityMarkerComposesWithWeaponAndArmorThroughDeployment()
  {
    var data = MakeFirearmWeaponData(magazine: 3);
    data.Capabilities.Add(new UtilityCapabilityData());
    var weapon = (FirearmWeapon)ItemRuntimeFactory.Create(data);
    using var campaign = new GeoscapeFixture(MakeStart(roster: [MakeEntry()]));
    var unit = campaign.State.Roster[0];
    var armorData = MakeArmorData("Shield Gadget");
    armorData.Capabilities.Add(new UtilityCapabilityData());
    var armor = ItemRuntimeFactory.Create(armorData);
    unit.EquipArmor(armor.With<ArmorCapability>().RequireSome());
    unit.EquipItem(unit.UnequipArmor().RequireSome().Item.With<UtilityCapability>().RequireSome(), 0);
    unit.EquipItem(weapon.With<UtilityCapability>().RequireSome(), 4);
    using var battle = BattleFixture.Started(new Vector3I(2, 1, 2),
      new UnitPlacement(new UnitLoadout(unit), Vector3I.Zero));

    Assert.Equal(2, battle.Unit.Inventory.Count);
    Assert.True(ReferenceEquals(armor, battle.Unit.Inventory[0]));
    Assert.True(ReferenceEquals(weapon, battle.Unit.Inventory[1]));
    Assert.True(weapon.TrySpendShot().IsSome);
    Assert.Equal(2, weapon.CurrentAmmo);
    var removed = unit.UnequipItem(4).RequireSome();
    Assert.True(ReferenceEquals(weapon, removed.Item));
    unit.EquipWeapon((Weapon)removed.Item);
    Assert.Equal(2, ((FirearmWeapon)unit.EquippedWeapon.RequireSome()).CurrentAmmo);
  }

  [TestCase("frag_grenade", true)]
  [TestCase("prototype_kit", true)]
  [TestCase("supply_kit", true)]
  [TestCase("vest", false)]
  [TestCase("workshop_sample", false)]
  public static void AuthoredUtilityMembershipIsExplicit(string resource, bool utility)
  {
    var data = GD.Load<EquippableItemData>($"res://resources/items/{resource}.tres");
    Assert.Equal(utility, ItemRuntimeFactory.Create(data).With<UtilityCapability>().IsSome);
  }
}
