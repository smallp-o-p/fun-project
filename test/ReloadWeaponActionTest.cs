using FunProject.Battle;
using FunProject.Weapons;
using GdUnit4;
using Godot;

namespace FunProject.Tests;

[TestSuite]
[RequireGodotRuntime]
public class ReloadWeaponActionTest
{
  [TestCase(TestName = "Reload refills the magazine, spends AP, and raises the reload event")]
  public static void ReloadRefillsSpendsApAndRaisesEvent()
  {
    var weapon = TestData.MakeAmmoWeapon("SMG", magazine: 3, damage: 1);
    using var battle = BattleFixture.Duel(
      hitChanceCalculator: new AlwaysHitCalculator(),
      player: new("Alpha", Weapon: weapon));
    var unit = battle.PlayerUnit;

    battle.Attack(unit, battle.EnemyUnit);
    Assert.Equal(2, weapon.CurrentAmmo);

    battle.ClearEvents();
    int actionPointsBefore = unit.CurrentActionPoints;

    battle.Reload(unit, weapon);

    Assert.Equal(3, weapon.CurrentAmmo);
    Assert.Equal(actionPointsBefore - BattleSession.DefaultReloadActionPointCost, unit.CurrentActionPoints);
    var reloadEvent = battle.Events.SingleEvent<UnitReloadedWeaponBattleEvent>();
    Assert.Equal(unit, reloadEvent.Unit);
  }

  [TestCase(TestName = "Reloading a full magazine is rejected and spends nothing")]
  public static void FullMagazineReloadRejected()
  {
    var faction = TestData.MakeFaction("Player");
    var weapon = TestData.MakeAmmoWeapon("SMG");
    using var battle = new BattleFixture(new BattleSetup(
      TestData.MakeOpenBattleMap(new Vector3I(4, 1, 4)),
      [new BattleSideSetup(faction, [new FakeObjectiveData()],
        [new UnitPlacement(new UnitLoadout(TestData.MakeCombatant("Alpha", faction), Some<Weapon>(weapon)), new Vector3I(0, 0, 0))])],
      Seed: 7));
    var unit = battle.Unit;

    int actionPointsBefore = unit.CurrentActionPoints;

    // Parameters are trusted: a reload the caller should have gated surfaces as the
    // executor's invariant-break throw.
    Assert.Throws<System.InvalidOperationException>(
      () => battle.Submit(BattleAction.ReloadWeapon(battle.Alive(unit), weapon)));

    Assert.Equal(actionPointsBefore, unit.CurrentActionPoints);
    Assert.Equal(weapon.MagazineSize, weapon.CurrentAmmo);
  }
}
