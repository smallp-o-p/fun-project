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
    var weapon = BattleTestFactory.MakeAmmoWeapon("SMG", magazine: 3, damage: 1);
    var battle = new BattleDuelBuilder
    {
      HitChanceCalculator = new AlwaysHitCalculator(),
      Player = new DuelSide("Alpha", Weapon: weapon),
    }.Start();
    var unit = battle.PlayerUnit;

    Attack(battle.Session, battle.Executor, unit.State, battle.EnemyUnit.State);
    Assert.Equal(2, weapon.CurrentAmmo);

    var recorder = new BattleEventRecorder(battle.Session);
    int actionPointsBefore = unit.CurrentActionPoints;

    battle.Executor.Submit(BattleAction.ReloadWeapon(unit.AliveIn(battle.Session), weapon));

    Assert.Equal(3, weapon.CurrentAmmo);
    Assert.Equal(actionPointsBefore - BattleSession.DefaultReloadActionPointCost, unit.CurrentActionPoints);
    var reloadEvent = recorder.Single<UnitReloadedWeaponBattleEvent>();
    Assert.Equal(unit.State, reloadEvent.Unit);
  }

  [TestCase(TestName = "Reloading a full magazine is rejected and spends nothing")]
  public void FullMagazineReloadRejected()
  {
    var faction = BattleTestFactory.MakeFaction("Player");
    var session = BattleTestFactory.MakeSession(new Vector3I(4, 1, 4), [faction]);
    var weapon = BattleTestFactory.MakeAmmoWeapon("SMG");
    var unit = SpawnUnit(session, BattleTestFactory.MakeCombatant("Alpha", faction), new Vector3I(0, 0, 0), weapon);
    StartBattle(session);

    int actionPointsBefore = unit.CurrentActionPoints;

    // Parameters are trusted: a reload the caller should have gated surfaces as the
    // executor's invariant-break throw.
    Assert.Throws<System.InvalidOperationException>(
      () => ExecutorFor(session).Submit(BattleAction.ReloadWeapon(unit.AliveIn(session), weapon)));

    Assert.Equal(actionPointsBefore, unit.CurrentActionPoints);
    Assert.Equal(weapon.MagazineSize, weapon.CurrentAmmo);
  }
}
