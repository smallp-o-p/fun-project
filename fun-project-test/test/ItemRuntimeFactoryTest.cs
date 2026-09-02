using FunProject.Items;
using FunProject.Weapons;
using Godot;
using GdUnit4;
using static FunProject.Tests.GeoscapeTestFactory;
using static GdUnit4.Assertions;
using System;

[TestSuite]
[RequireGodotRuntime]
public class ItemRuntimeFactoryTest
{
  [TestCase(TestName = "Firearm data creates a FirearmWeapon carrying its Data")]
  public void FirearmCreatesFirearm()
  {
    FirearmWeaponData data = MakeFirearmData();

    EquippableItem item = ItemRuntimeFactory.Create(data);

    Assert.True(item is FirearmWeapon);
    AssertThat(data).IsSame(item.Data);
  }

  [TestCase(TestName = "Ammunitioned (non-firearm) data creates an AmmunitionedWeapon")]
  public void AmmunitionedCreatesAmmunitioned()
  {
    EquippableItem item = ItemRuntimeFactory.Create(MakeAmmunitionedData());

    Assert.True(item is AmmunitionedWeapon);
    Assert.False(item is FirearmWeapon);
  }

  [TestCase(TestName = "Plain weapon data creates a Weapon")]
  public void PlainWeaponCreatesWeapon()
  {
    EquippableItem item = ItemRuntimeFactory.CreateWeapon(MakePlainWeaponData());

    Assert.True(item is Weapon);
    Assert.False(item is AmmunitionedWeapon);
  }

  [TestCase(TestName = "Bare item data creates a plain EquippableItem")]
  public void BareDataCreatesPlainItem()
  {
    EquippableItem item = ItemRuntimeFactory.Create(new EquippableItemData());

    Assert.Equal(typeof(EquippableItem), item.GetType());
  }

  [TestCase(TestName = "Null data throws")]
  public void NullThrows()
  {
    Assert.Throws<ArgumentNullException>(() => ItemRuntimeFactory.Create(null!));
  }
}
