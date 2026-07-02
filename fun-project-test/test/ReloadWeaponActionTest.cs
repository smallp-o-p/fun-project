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

    Assert.True(battle.Executor.Submit(BattleAction.AttackUnit(unit.State, battle.EnemyUnit.State)).RequireSingleResult().Succeeded);
    Assert.Equal(2, weapon.CurrentAmmo);

    var recorder = new BattleEventRecorder(battle.Session);
    int actionPointsBefore = unit.CurrentActionPoints;

    var result = battle.Executor.Submit(BattleAction.ReloadWeapon(unit.State)).RequireSingleResult();

    Assert.True(result.Succeeded);
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
    var unit = SpawnUnit(session, BattleTestFactory.MakeCombatant("Alpha", faction), new Vector3I(0, 0, 0), BattleTestFactory.MakeAmmoWeapon("SMG"));
    StartBattle(session);

    int actionPointsBefore = unit.CurrentActionPoints;
    var result = new BattleActionExecutor(session).Submit(BattleAction.ReloadWeapon(unit.State)).RequireSingleResult();

    Assert.False(result.Succeeded);
    Assert.Equal(BattleActionFailureReason.Rejected, result.FailureReason);
    Assert.Equal(actionPointsBefore, unit.CurrentActionPoints);
  }

  [TestCase(TestName = "A unit without a magazine weapon cannot reload")]
  public void NonMagazineWeaponReloadRejected()
  {
    var faction = BattleTestFactory.MakeFaction("Player");
    var session = BattleTestFactory.MakeSession(new Vector3I(4, 1, 4), [faction]);
    var armedWithMelee = SpawnUnit(session, BattleTestFactory.MakeCombatant("Alpha", faction), new Vector3I(0, 0, 0), BattleTestFactory.MakeWeapon("Sword"));
    var unarmed = SpawnUnit(session, BattleTestFactory.MakeCombatant("Bravo", faction), new Vector3I(1, 0, 0));
    StartBattle(session);

    var executor = new BattleActionExecutor(session);
    Assert.Equal(BattleActionFailureReason.Rejected, executor.Submit(BattleAction.ReloadWeapon(armedWithMelee.State)).RequireSingleResult().FailureReason);
    Assert.Equal(BattleActionFailureReason.Rejected, executor.Submit(BattleAction.ReloadWeapon(unarmed.State)).RequireSingleResult().FailureReason);
  }

  [TestCase(TestName = "An off-turn unit cannot reload")]
  public void OffTurnReloadRejected()
  {
    var battle = new BattleDuelBuilder
    {
      Enemy = new DuelSide("Goon", Weapon: BattleTestFactory.MakeAmmoWeapon("SMG")),
    }.Start();

    var result = battle.Executor.Submit(BattleAction.ReloadWeapon(battle.EnemyUnit.State)).RequireSingleResult();

    Assert.False(result.Succeeded);
    Assert.Equal(BattleActionFailureReason.Rejected, result.FailureReason);
  }
}
