using FunProject.Combatants;
using FunProject.Items;
using FunProject.Items.Capabilities;
using FunProject.Weapons;
using GdUnit4;
using LanguageExt.UnsafeValueAccess;
using static FunProject.Tests.GeoscapeTestFactory;
using static GdUnit4.Assertions;
using System;

[TestSuite]
[RequireGodotRuntime]
public class CombatantLoadoutTest
{
  private static Combatant MakeUnit() => new(MakeCombatantData(), new Faction(new FactionData()));

  private static Weapon MakeWeapon(string name) => ItemRuntimeFactory.CreateWeapon(MakeFirearmData(name));

  [TestCase(TestName = "EquipWeapon stores and returns no displacement on an empty slot")]
  public void EquipWeaponEmptySlot()
  {
    Combatant unit = MakeUnit();
    Weapon pistol = MakeWeapon("Pistol");

    Option<Weapon> displaced = unit.EquipWeapon(pistol);

    Assert.False(displaced.IsSome);
    AssertThat(unit.EquippedWeapon.ValueUnsafe()).IsSame(pistol);
  }

  [TestCase(TestName = "EquipWeapon returns the previously equipped weapon")]
  public void EquipWeaponDisplaces()
  {
    Combatant unit = MakeUnit();
    Weapon pistol = MakeWeapon("Pistol");
    Weapon rifle = MakeWeapon("Rifle");
    unit.EquipWeapon(pistol);

    Option<Weapon> displaced = unit.EquipWeapon(rifle);

    AssertThat(displaced.ValueUnsafe()).IsSame(pistol);
    AssertThat(unit.EquippedWeapon.ValueUnsafe()).IsSame(rifle);
  }

  [TestCase(TestName = "UnequipWeapon clears and returns the weapon")]
  public void UnequipWeapon()
  {
    Combatant unit = MakeUnit();
    Weapon pistol = MakeWeapon("Pistol");
    unit.EquipWeapon(pistol);

    Option<Weapon> removed = unit.UnequipWeapon();

    AssertThat(removed.ValueUnsafe()).IsSame(pistol);
    Assert.False(unit.EquippedWeapon.IsSome);
  }

  [TestCase(TestName = "EquipArmor stores the capability proof and displaces the old one")]
  public void EquipArmorDisplaces()
  {
    Combatant unit = MakeUnit();
    ItemWith<ArmorCapability> vestB = ItemRuntimeFactory.Create(MakeArmorData("Vest")).With<ArmorCapability>().ValueUnsafe();
    ItemWith<ArmorCapability> plate = ItemRuntimeFactory.Create(MakeArmorData("Plate")).With<ArmorCapability>().ValueUnsafe();

    Option<ItemWith<ArmorCapability>> first = unit.EquipArmor(vestB);
    Option<ItemWith<ArmorCapability>> second = unit.EquipArmor(plate);

    Assert.False(first.IsSome);
    AssertThat(second.ValueUnsafe().Item).IsSame(vestB.Item); // second returns the DISPLACED vest
    AssertThat(unit.EquippedArmor.ValueUnsafe().Item).IsSame(plate.Item);
  }

  [TestCase(TestName = "UnequipItem returns the utility item and clears the slot")]
  public void UnequipItem()
  {
    Combatant unit = MakeUnit();
    EquippableItem grenade = ItemRuntimeFactory.Create(new EquippableItemData { Name = "Grenade" });
    unit.EquipItem(grenade, 0);

    Option<EquippableItem> removed = unit.UnequipItem(0);

    AssertThat(removed.ValueUnsafe()).IsSame(grenade);
    Assert.False(unit.Inventory.ContainsKey(0));
  }

  [TestCase(TestName = "UnequipItem throws on out-of-range slot")]
  public void UnequipItemOutOfRangeThrows()
  {
    Combatant unit = MakeUnit();

    Assert.Throws<InvalidOperationException>(() => unit.UnequipItem(99));
  }
}
