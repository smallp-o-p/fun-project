using FunProject.Battle;
using FunProject.Tests;
using GdUnit4;
using Godot;
using System.Collections.Generic;
using System.Linq;
using static BattleActionTestHelper;

[TestSuite]
[RequireGodotRuntime]
public class BattleCausalityTest
{
  [TestCase(TestName = "An attack hit carries the attacker as the damage event's cause")]
  public void AttackHitCarriesAttackerAsDamageCause()
  {
    var playerFaction = BattleTestFactory.MakeFaction("Player");
    var enemyFaction = BattleTestFactory.MakeFaction("Enemy");
    var session = BattleTestFactory.MakeSession(new Vector3I(8, 1, 8), [playerFaction, enemyFaction], new AlwaysHitCalculator());
    var attacker = SpawnUnit(session, BattleTestFactory.MakeCombatant("Alpha", playerFaction), new Vector3I(4, 0, 1), BattleTestFactory.MakeWeapon("Rifle", damage: 5));
    var target = SpawnUnit(session, BattleTestFactory.MakeCombatant("Hostile", enemyFaction), new Vector3I(4, 0, 4));
    StartBattle(session);

    var raisedEvents = new List<BattleEvent>();
    session.BattleEventCommitted += raisedEvents.Add;

    var executor = new BattleActionExecutor(session);
    Assert.True(executor.Submit(BattleAction.AttackUnit(attacker.State, target.State)).RequireSingleResult().Succeeded);

    var damagedEvent = raisedEvents.OfType<UnitDamagedBattleEvent>().Single();
    Assert.Equal(attacker.State, damagedEvent.MaybeCause.RequireSome());
  }

  [TestCase(TestName = "A killing blow carries the attacker on both the damage and kill events")]
  public void KillingBlowCarriesAttackerOnDamageAndKillEvents()
  {
    var playerFaction = BattleTestFactory.MakeFaction("Player");
    var enemyFaction = BattleTestFactory.MakeFaction("Enemy");
    var session = BattleTestFactory.MakeSession(new Vector3I(8, 1, 8), [playerFaction, enemyFaction], new AlwaysHitCalculator());
    var attacker = SpawnUnit(session, BattleTestFactory.MakeCombatant("Alpha", playerFaction), new Vector3I(4, 0, 1), BattleTestFactory.MakeWeapon("Rifle", damage: 5));
    var target = SpawnUnit(session, BattleTestFactory.MakeCombatant("Hostile", enemyFaction, health: 5), new Vector3I(4, 0, 4));
    StartBattle(session);

    var raisedEvents = new List<BattleEvent>();
    session.BattleEventCommitted += raisedEvents.Add;

    var executor = new BattleActionExecutor(session);
    Assert.True(executor.Submit(BattleAction.AttackUnit(attacker.State, target.State)).RequireSingleResult().Succeeded);

    Assert.True(target.State.IsDead);
    Assert.Equal(attacker.State, raisedEvents.OfType<UnitDamagedBattleEvent>().Single().MaybeCause.RequireSome());
    var killedEvent = raisedEvents.OfType<UnitKilledBattleEvent>().Single();
    Assert.Equal(target.State, killedEvent.Unit);
    Assert.Equal(attacker.State, killedEvent.MaybeCause.RequireSome());
  }

  [TestCase(TestName = "Direct damage without an attacker leaves the cause empty on damage and kill events")]
  public void DirectDamageLeavesCauseEmpty()
  {
    var faction = BattleTestFactory.MakeFaction("Player");
    var session = BattleTestFactory.MakeSession(new Vector3I(4, 1, 4), [faction]);
    var unit = SpawnUnit(session, BattleTestFactory.MakeCombatant("Alpha", faction, health: 20), new Vector3I(0, 0, 0));
    StartBattle(session);

    var raisedEvents = new List<BattleEvent>();
    session.BattleEventCommitted += raisedEvents.Add;

    var executor = new BattleActionExecutor(session);
    Assert.True(executor.Submit(BattleAction.ApplyDamage(unit.State, 3)).RequireSingleResult().Succeeded);
    Assert.True(executor.Submit(BattleAction.ApplyDamage(unit.State, 999)).RequireSingleResult().Succeeded);

    Assert.True(raisedEvents.OfType<UnitDamagedBattleEvent>().All(damagedEvent => damagedEvent.MaybeCause.IsNone));
    Assert.True(raisedEvents.OfType<UnitKilledBattleEvent>().Single().MaybeCause.IsNone);
  }
}
