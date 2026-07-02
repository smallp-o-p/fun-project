using FunProject.Battle;
using FunProject.Tests;
using GdUnit4;
using Godot;
using System.Collections.Generic;
using System.Linq;
using static BattleActionTestHelper;

[TestSuite]
[RequireGodotRuntime]
public class ReloadWeaponActionTest
{
  [TestCase(TestName = "Reload refills the magazine, spends AP, and raises the reload event")]
  public void ReloadRefillsSpendsApAndRaisesEvent()
  {
    var playerFaction = BattleTestFactory.MakeFaction("Player");
    var enemyFaction = BattleTestFactory.MakeFaction("Enemy");
    var session = BattleTestFactory.MakeSession(new Vector3I(8, 1, 8), [playerFaction, enemyFaction], new AlwaysHitCalculator());
    var weapon = BattleTestFactory.MakeAmmoWeapon("SMG", magazine: 3, damage: 1);
    var unit = SpawnUnit(session, BattleTestFactory.MakeCombatant("Alpha", playerFaction), new Vector3I(4, 0, 1), weapon);
    var target = SpawnUnit(session, BattleTestFactory.MakeCombatant("Hostile", enemyFaction), new Vector3I(4, 0, 4));
    StartBattle(session);

    var executor = new BattleActionExecutor(session);
    Assert.True(executor.Submit(BattleAction.AttackUnit(unit.State, target.State)).RequireSingleResult().Succeeded);
    Assert.Equal(2, weapon.CurrentAmmo);

    var raisedEvents = new List<BattleEvent>();
    session.BattleEventCommitted += raisedEvents.Add;
    int actionPointsBefore = unit.CurrentActionPoints;

    var result = executor.Submit(BattleAction.ReloadWeapon(unit.State)).RequireSingleResult();

    Assert.True(result.Succeeded);
    Assert.Equal(3, weapon.CurrentAmmo);
    Assert.Equal(actionPointsBefore - BattleSession.DefaultReloadActionPointCost, unit.CurrentActionPoints);
    var reloadEvent = raisedEvents.OfType<UnitReloadedWeaponBattleEvent>().Single();
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
    var playerFaction = BattleTestFactory.MakeFaction("Player");
    var enemyFaction = BattleTestFactory.MakeFaction("Enemy");
    var session = BattleTestFactory.MakeSession(new Vector3I(8, 1, 8), [playerFaction, enemyFaction]);
    SpawnUnit(session, BattleTestFactory.MakeCombatant("Alpha", playerFaction), new Vector3I(0, 0, 0));
    var goon = SpawnUnit(session, BattleTestFactory.MakeCombatant("Goon", enemyFaction), new Vector3I(3, 0, 0), BattleTestFactory.MakeAmmoWeapon("SMG"));
    StartBattle(session); // player faction is active first

    var result = new BattleActionExecutor(session).Submit(BattleAction.ReloadWeapon(goon.State)).RequireSingleResult();

    Assert.False(result.Succeeded);
    Assert.Equal(BattleActionFailureReason.Rejected, result.FailureReason);
  }
}
