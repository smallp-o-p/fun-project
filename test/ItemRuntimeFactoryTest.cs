using FunProject.Items;
using FunProject.Weapons;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;
using System;

namespace FunProject.Tests;

[TestSuite]
[RequireGodotRuntime]
public class ItemRuntimeFactoryTest
{
  [TestCase(TestName = "Firearm data creates a FirearmWeapon carrying its Data")]
  public static void FirearmCreatesFirearm()
  {
    FirearmWeaponData data = TestData.MakeFirearmWeaponData("Test Pistol",
      damage: 4, critChance: 10, range: 6, magazine: 6, ammo: new Ammunition { Name = "Test Rounds" });

    EquippableItem item = ItemRuntimeFactory.Create(data);

    Assert.True(item is FirearmWeapon);
    AssertThat(data).IsSame(item.Data);
  }

  [TestCase(TestName = "Ammunitioned (non-firearm) data creates an AmmunitionedWeapon")]
  public static void AmmunitionedCreatesAmmunitioned()
  {
    EquippableItem item = ItemRuntimeFactory.Create(TestData.MakeAmmoWeaponData("Test Rifle",
      magazine: 8, damage: 5, range: 8, critChance: 10,
      ammo: new Ammunition { Name = "Test Rounds" }, description: "Description"));

    Assert.True(item is AmmunitionedWeapon);
    Assert.False(item is FirearmWeapon);
  }

  [TestCase(TestName = "Plain weapon data creates a Weapon")]
  public static void PlainWeaponCreatesWeapon()
  {
    EquippableItem item = ItemRuntimeFactory.CreateWeapon(TestData.MakeWeaponData(
      damage: 4, critChance: 10, range: 6, name: "Test Weapon"));

    Assert.True(item is Weapon);
    Assert.False(item is AmmunitionedWeapon);
  }

  [TestCase(TestName = "Bare item data creates a plain EquippableItem")]
  public static void BareDataCreatesPlainItem()
  {
    EquippableItem item = ItemRuntimeFactory.Create(new EquippableItemData());

    Assert.Equal(typeof(EquippableItem), item.GetType());
  }

  [TestCase(TestName = "Null data throws")]
  public static void NullThrows()
  {
    Assert.Throws<ArgumentNullException>(() => ItemRuntimeFactory.Create(null!));
  }
}
