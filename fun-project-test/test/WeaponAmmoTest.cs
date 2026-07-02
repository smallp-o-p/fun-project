using FunProject.Battle;
using FunProject.Weapons;
using GdUnit4;

[TestSuite]
[RequireGodotRuntime]
public class WeaponAmmoTest
{
  [TestCase(TestName = "Spending a shot decrements only this weapon instance, never the authored template")]
  public void SpendingNeverMutatesTemplateOrSiblings()
  {
    var data = BattleTestFactory.MakeAmmoWeaponData("SMG", magazine: 6);
    var weaponA = new AmmunitionedWeapon(data);
    var weaponB = new AmmunitionedWeapon(data);

    Assert.True(weaponA.TrySpendShot().IsSome);

    Assert.Equal(5, weaponA.CurrentAmmo); // default frame = 1 packet -> ShotCost 1
    Assert.Equal(6, weaponB.CurrentAmmo);
    Assert.Equal(6, data.AmmunitionStat.BaseValue);
  }

  [TestCase(TestName = "TrySpendShot returns None once the magazine cannot afford a shot")]
  public void TrySpendShotReturnsNoneWhenEmpty()
  {
    var weapon = BattleTestFactory.MakeAmmoWeapon("Pistol", magazine: 1);

    Assert.True(weapon.TrySpendShot().IsSome);
    Assert.Equal(0, weapon.CurrentAmmo);
    Assert.True(weapon.NeedsToReload());
    Assert.True(weapon.TrySpendShot().IsNone);
    Assert.Equal(0, weapon.CurrentAmmo);
  }

  [TestCase(TestName = "Reload refills to magazine size and leaves the authored stat untouched")]
  public void ReloadRefillsToMagazineSize()
  {
    var data = BattleTestFactory.MakeAmmoWeaponData("Rifle", magazine: 4);
    var weapon = new AmmunitionedWeapon(data);

    Assert.True(weapon.TrySpendShot().IsSome);
    Assert.True(weapon.CanReload());

    weapon.Reload();

    Assert.Equal(4, weapon.CurrentAmmo);
    Assert.False(weapon.CanReload());
    Assert.Equal(4, data.AmmunitionStat.BaseValue);
  }

  [TestCase(TestName = "A melee weapon always spends a shot successfully")]
  public void MeleeAlwaysSpends()
  {
    var weapon = BattleTestFactory.MakeWeapon("Sword");

    Assert.True(weapon.TrySpendShot().IsSome);
    Assert.True(weapon.TrySpendShot().IsSome);
  }
}
