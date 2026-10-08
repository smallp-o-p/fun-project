using System;
using FunProject.Items;
using FunProject.Items.Capabilities;
using FunProject.Weapons;
using Godot;
using GdUnit4;

namespace FunProject.Tests;

[TestSuite]
[RequireGodotRuntime]
public class EquipmentSlotCompatibilityTest
{
  [TestCase]
  public static void UtilityEquipRequiresAuthoredCompatibility()
  {
    using var campaign = new GeoscapeFixture(MakeStart(roster: [MakeEntry()]));
    var unit = campaign.State.Roster[0];
    var kit = new EquippableItem(MakeUtilityData("Field Kit"));
    unit.EquipItem(kit.ForUtilitySlot().RequireSome(), 0);

    EquippableItemData[] rejected = [MakeWeaponData(), MakeArmorData("Vest"), MakeItemData("Workshop Sample")];
    foreach (EquippableItemData data in rejected)
      Assert.True(ItemRuntimeFactory.Create(data).ForUtilitySlot().IsNone,
        $"'{data.Name}' must not mint a utility equipment proof.");

    Assert.True(ReferenceEquals(kit, unit.Inventory[0]));
    Assert.True(ReferenceEquals(kit, unit.UnequipItem(0).RequireSome()));
    Assert.Throws<InvalidOperationException>(() => unit.EquipItem(kit.ForUtilitySlot().RequireSome(), -1));
    Assert.Throws<InvalidOperationException>(() => unit.EquipItem(kit.ForUtilitySlot().RequireSome(), unit.MaxInventorySize));
    Assert.Equal(0, unit.Inventory.Count);
  }

  [TestCase]
  public static void CompatibilityCanOverlapAndPreservesWeaponRuntime()
  {
    var data = MakeFirearmWeaponData(magazine: 3);
    data.CompatibleSlots = EquipmentSlot.Weapon | EquipmentSlot.Utility;
    var weapon = (FirearmWeapon)ItemRuntimeFactory.Create(data);
    UtilityItem utility = weapon.ForUtilitySlot().RequireSome();
    using var campaign = new GeoscapeFixture(MakeStart(roster: [MakeEntry()]));
    var unit = campaign.State.Roster[0];

    unit.EquipItem(utility, 0);

    Assert.True(ReferenceEquals(weapon, unit.Inventory[0]));
    Assert.True(weapon.TrySpendShot().IsSome);
    Assert.Equal(2, weapon.CurrentAmmo);
    unit.UnequipItem(0);
    unit.EquipWeapon(weapon);
    Assert.Equal(2, ((FirearmWeapon)unit.EquippedWeapon.RequireSome()).CurrentAmmo);
  }

  [TestCase]
  public static void IncompatibleWeaponAndArmorCannotDisplaceEquippedGear()
  {
    using var campaign = new GeoscapeFixture(MakeStart(roster: [MakeEntry()]));
    var unit = campaign.State.Roster[0];
    var weapon = MakeWeapon("Rifle");
    var armor = MakeArmor("Vest");
    unit.EquipWeapon(weapon);
    unit.EquipArmor(armor);
    var otherWeaponData = MakeWeaponData();
    otherWeaponData.CompatibleSlots = EquipmentSlot.Utility;
    var otherArmorData = MakeArmorData("Shield Gadget");
    otherArmorData.CompatibleSlots = EquipmentSlot.Utility;
    var otherArmor = ItemRuntimeFactory.Create(otherArmorData).With<ArmorCapability>().RequireSome();

    Assert.Throws<InvalidOperationException>(() => unit.EquipWeapon(ItemRuntimeFactory.CreateWeapon(otherWeaponData)));
    Assert.Throws<InvalidOperationException>(() => unit.EquipArmor(otherArmor));

    Assert.True(ReferenceEquals(weapon, unit.EquippedWeapon.RequireSome()));
    Assert.True(ReferenceEquals(armor.Item, unit.EquippedArmor.RequireSome().Item));
    unit.EquipItem(otherArmor.Item.ForUtilitySlot().RequireSome(), 0);
    Assert.True(ReferenceEquals(otherArmor.Item, unit.Inventory[0]));
  }

  [TestCase]
  public static void RuntimeCompatibilityIsCapturedBeforeProofMinting()
  {
    var data = MakeUtilityData();
    var item = ItemRuntimeFactory.Create(data);
    data.CompatibleSlots = EquipmentSlot.None;
    Assert.True(item.ForUtilitySlot().IsSome);

    var incompatible = ItemRuntimeFactory.Create(data);
    data.CompatibleSlots = EquipmentSlot.Utility;
    Assert.True(incompatible.ForUtilitySlot().IsNone);
  }

  [TestCase("frag_grenade", true)]
  [TestCase("prototype_kit", true)]
  [TestCase("supply_kit", true)]
  [TestCase("vest", false)]
  [TestCase("workshop_sample", false)]
  public static void AuthoredUtilityMembershipIsExplicit(string resource, bool utility)
  {
    var data = GD.Load<EquippableItemData>($"res://resources/items/{resource}.tres");
    Assert.Equal(utility, ItemRuntimeFactory.Create(data).ForUtilitySlot().IsSome);
  }
}
