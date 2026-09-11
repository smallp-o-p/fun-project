using FunProject.Battle;
using GdUnit4;
using Godot;

[TestSuite]
[RequireGodotRuntime]
public class ReloadWeaponActionTest
{
  [TestCase(TestName = "Reload refills the magazine, spends AP, and raises the reload event")]
  public void ReloadRefillsSpendsApAndRaisesEvent()
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
  public void FullMagazineReloadRejected()
  {
    var faction = TestData.MakeFaction("Player");
    using var battle = new BattleFixture(new Vector3I(4, 1, 4), [faction]);
    var weapon = TestData.MakeAmmoWeapon("SMG");
    var unit = battle.Spawn(TestData.MakeCombatant("Alpha", faction), new Vector3I(0, 0, 0), weapon);
    battle.Start();

    int actionPointsBefore = unit.CurrentActionPoints;

    // Parameters are trusted: a reload the caller should have gated surfaces as the
    // executor's invariant-break throw.
    Assert.Throws<System.InvalidOperationException>(
      () => battle.Submit(BattleAction.ReloadWeapon(battle.Alive(unit), weapon)));

    Assert.Equal(actionPointsBefore, unit.CurrentActionPoints);
    Assert.Equal(weapon.MagazineSize, weapon.CurrentAmmo);
  }
}
