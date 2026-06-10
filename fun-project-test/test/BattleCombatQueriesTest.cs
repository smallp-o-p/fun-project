using FunProject.Battle;
using FunProject.Tests;
using GdUnit4;
using Godot;
using System.Collections.Generic;
using System.Linq;
using static BattleActionTestHelper;

[TestSuite]
[RequireGodotRuntime]
public partial class BattleCombatQueriesTest
{
  [TestCase(TestName = "GetHitChanceForAttack returns the cover-modified breakdown")]
  public void GetHitChanceForAttackReturnsTheCoverModifiedBreakdown()
  {
    var playerFaction = BattleTestFactory.MakeFaction("Player");
    var enemyFaction = BattleTestFactory.MakeFaction("Enemy");
    var board = new BattleBoardState(new Vector3I(8, 1, 8));
    var targetPoint = board.ValidatePoint(new Vector3I(4, 0, 4)).RequireSome();
    board.GetTile(targetPoint).Cover = new TileCover(CoverDirections.North, 40);
    var session = BattleTestFactory.MakeSession(board, [playerFaction, enemyFaction]);
    var attacker = SpawnUnit(session, BattleTestFactory.MakeCombatant("Alpha", playerFaction, aim: 65), new Vector3I(4, 0, 1), BattleTestFactory.MakeWeapon("Rifle"));
    var target = SpawnUnit(session, BattleTestFactory.MakeCombatant("Hostile", enemyFaction), new Vector3I(4, 0, 4));
    StartBattle(session);

    var breakdown = BattleQueryTestHelper.GetValue(
      BattleQueryTestHelper.Query(session, new GetHitChanceForAttack(attacker.State, target.State)));

    Assert.Equal(65, breakdown.BaseChance);
    Assert.Equal(-40, breakdown.Modifiers.Single().Amount);
    Assert.Equal(25, breakdown.FinalChance);
  }

  [TestCase(TestName = "GetHitChanceForAttack ignores turn order")]
  public void GetHitChanceForAttackIgnoresTurnOrder()
  {
    var playerFaction = BattleTestFactory.MakeFaction("Player");
    var enemyFaction = BattleTestFactory.MakeFaction("Enemy");
    var session = BattleTestFactory.MakeSession(new Vector3I(8, 1, 8), [playerFaction, enemyFaction]);
    SpawnUnit(session, BattleTestFactory.MakeCombatant("Alpha", playerFaction), new Vector3I(4, 0, 1));
    var enemyAttacker = SpawnUnit(session, BattleTestFactory.MakeCombatant("Hostile", enemyFaction, aim: 70), new Vector3I(4, 0, 4), BattleTestFactory.MakeWeapon("Rifle"));
    var playerTarget = SpawnUnit(session, BattleTestFactory.MakeCombatant("Bravo", playerFaction), new Vector3I(4, 0, 6));
    StartBattle(session);

    // Player faction is active; the enemy attacker can still preview its odds.
    var breakdown = BattleQueryTestHelper.GetValue(
      BattleQueryTestHelper.Query(session, new GetHitChanceForAttack(enemyAttacker.State, playerTarget.State)));

    Assert.Equal(70, breakdown.FinalChance);
  }

  [TestCase(TestName = "GetHitChanceForAttack matches the breakdown the attack resolves with")]
  public void GetHitChanceForAttackMatchesTheBreakdownTheAttackResolvesWith()
  {
    var playerFaction = BattleTestFactory.MakeFaction("Player");
    var enemyFaction = BattleTestFactory.MakeFaction("Enemy");
    var board = new BattleBoardState(new Vector3I(8, 1, 8));
    var targetPoint = board.ValidatePoint(new Vector3I(4, 0, 4)).RequireSome();
    board.GetTile(targetPoint).Cover = new TileCover(CoverDirections.North, 40);
    var session = BattleTestFactory.MakeSession(board, [playerFaction, enemyFaction], randomSeed: 99);
    var attacker = SpawnUnit(session, BattleTestFactory.MakeCombatant("Alpha", playerFaction, aim: 65), new Vector3I(4, 0, 1), BattleTestFactory.MakeWeapon("Rifle"));
    var target = SpawnUnit(session, BattleTestFactory.MakeCombatant("Hostile", enemyFaction), new Vector3I(4, 0, 4));
    StartBattle(session);

    var previewed = BattleQueryTestHelper.GetValue(
      BattleQueryTestHelper.Query(session, new GetHitChanceForAttack(attacker.State, target.State)));

    var raisedEvents = new List<BattleEvent>();
    session.BattleEventCommitted += raisedEvents.Add;
    var executor = new BattleActionExecutor(session);
    Assert.True(executor.Submit(BattleAction.AttackUnit(attacker.State, target.State)).RequireSingleResult().Succeeded);

    var resolved = raisedEvents.OfType<UnitAttackedBattleEvent>().Single().Breakdown;
    Assert.Equal(previewed.BaseChance, resolved.BaseChance);
    Assert.Equal(previewed.FinalChance, resolved.FinalChance);
    Assert.Equal(previewed.Modifiers.Count, resolved.Modifiers.Count);
  }

  [TestCase(TestName = "GetHitChanceForAttack fails when the odds are undefined")]
  public void GetHitChanceForAttackFailsWhenTheOddsAreUndefined()
  {
    var playerFaction = BattleTestFactory.MakeFaction("Player");
    var enemyFaction = BattleTestFactory.MakeFaction("Enemy");
    var session = BattleTestFactory.MakeSession(new Vector3I(8, 1, 8), [playerFaction, enemyFaction]);
    var armedAttacker = SpawnUnit(session, BattleTestFactory.MakeCombatant("Alpha", playerFaction), new Vector3I(4, 0, 1), BattleTestFactory.MakeWeapon("Rifle"));
    var unarmedAttacker = SpawnUnit(session, BattleTestFactory.MakeCombatant("Bravo", playerFaction), new Vector3I(5, 0, 1));
    var shortSightedAttacker = SpawnUnit(session, BattleTestFactory.MakeCombatant("Charlie", playerFaction, vision: 1), new Vector3I(6, 0, 1), BattleTestFactory.MakeWeapon("Rifle"));
    var shortRangedAttacker = SpawnUnit(session, BattleTestFactory.MakeCombatant("Delta", playerFaction), new Vector3I(7, 0, 1), BattleTestFactory.MakeWeapon("Pistol", range: 1));
    var target = SpawnUnit(session, BattleTestFactory.MakeCombatant("Hostile", enemyFaction), new Vector3I(4, 0, 4));
    StartBattle(session);

    void AssertFails(BattleUnitState attacker, BattleUnitState attackTarget, BattleQueryFailureReason expectedReason)
    {
      var failure = BattleQueryTestHelper.GetFailure(
        BattleQueryTestHelper.Query(session, new GetHitChanceForAttack(attacker, attackTarget)));
      Assert.Equal(expectedReason, failure.Reason);
    }

    AssertFails(unarmedAttacker.State, target.State, BattleQueryFailureReason.InvalidBattleState);   // no weapon
    AssertFails(armedAttacker.State, armedAttacker.State, BattleQueryFailureReason.InvalidBattleState); // self-target
    AssertFails(armedAttacker.State, unarmedAttacker.State, BattleQueryFailureReason.InvalidBattleState); // allied target
    AssertFails(shortSightedAttacker.State, target.State, BattleQueryFailureReason.InvalidBattleState); // not visible
    AssertFails(shortRangedAttacker.State, target.State, BattleQueryFailureReason.InvalidBattleState);  // out of range

    var executor = new BattleActionExecutor(session);
    executor.Submit(BattleAction.ApplyDamage(target.State, 999));
    AssertFails(armedAttacker.State, target.State, BattleQueryFailureReason.UnitNotAlive);           // dead target

    // A dead attacker fails before the ally guard can report InvalidBattleState.
    executor.Submit(BattleAction.ApplyDamage(armedAttacker.State, 999));
    AssertFails(armedAttacker.State, unarmedAttacker.State, BattleQueryFailureReason.UnitNotAlive);  // dead attacker
  }
}
