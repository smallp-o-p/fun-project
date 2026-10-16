#nullable disable warnings
using FunProject.Items.Capabilities;
using FunProject.Stats;
using FunProject.Weapons;
using GdUnit4;

namespace FunProject.Tests;

[TestSuite]
[RequireGodotRuntime]
public class WeaponSystemTest
{
  [TestCase(TestName = "Weapon generic stat lookup returns concrete stats")]
  public static void WeaponGenericStatLookupReturnsConcreteStats()
  {
    var weapon = new MeleeWeapon(TestData.MakeWeaponData());

    Assert.True(weapon.TryGetStat<AmmunitionStat>().IsNone);
    Assert.Equal(10, weapon.GetStat<DamageStat>().BaseValue);
    Option<RangeStat> range = weapon.TryGetStat<RangeStat>();
    Assert.True(range.IsSome);
    Assert.Equal(2, range.RequireSome().BaseValue);
    Assert.True(weapon.TryGetStat<HealthStat>().IsNone);
  }

  [TestCase(TestName = "Firearm generic stat lookup returns the ammunition stat")]
  public static void FirearmGenericStatLookupReturnsAmmunitionStat()
  {
    var weapon = new FirearmWeapon(TestData.MakeFirearmWeaponData(range: 1, ammo: new Ammunition(), modSlots: 1));

    Assert.Equal(12, weapon.MagazineSize);
    Assert.Equal(FirearmArchetype.Pistol, weapon.Archetype);
    Option<AmmunitionStat> ammoStat = weapon.TryGetStat<AmmunitionStat>();
    Assert.True(ammoStat.IsSome);
    Assert.Equal(12, ammoStat.RequireSome().BaseValue);
    Assert.True(weapon.TryGetStat<HealthStat>().IsNone);
  }

  [TestCase(TestName = "ModSlot can equip and unequip")]
  public static void ModSlotCanEquipAndUnequip()
  {
    var slot = new ModSlot();
    Assert.False(slot.HasMod);
    var mod = new MultiStatMod();
    slot.Equip(mod);
    Assert.True(slot.HasMod);
    var returned = slot.Unequip();
    Assert.Equal(mod, returned.RequireSome());
    Assert.False(slot.HasMod);
  }

}
