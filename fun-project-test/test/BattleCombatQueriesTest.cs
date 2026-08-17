using FunProject.Battle;
using GdUnit4;
using Godot;
using System.Collections.Generic;

[TestSuite]
[RequireGodotRuntime]
public partial class BattleCombatQueriesTest
{
  [TestCase(TestName = "GetHitChanceForAttack returns the cover-modified breakdown")]
  public void GetHitChanceForAttackReturnsTheCoverModifiedBreakdown()
  {
    var board = new BattleBoardState(new Vector3I(8, 1, 8));
    board.GetTile(board.At(4, 0, 4)).Cover = new TileCover(CoverDirections.North, 40);
    var battle = new BattleDuelBuilder
    {
      Board = board,
      Player = new DuelSide("Alpha", Weapon: BattleTestFactory.MakeWeapon("Rifle")),
    }.Start();

    var breakdown = BattleQueryTestHelper.GetValue(
      BattleQueryTestHelper.Query(battle.Session, new GetHitChanceForAttack(
        battle.PlayerUnit.AliveIn(battle.Session), battle.EnemyUnit.AliveIn(battle.Session))));

    Assert.Equal(65, breakdown.BaseChance);
    Assert.Equal(-40, breakdown.Modifiers.AsValueEnumerable().Single().Amount);
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
      BattleQueryTestHelper.Query(session, new GetHitChanceForAttack(
        enemyAttacker.AliveIn(session), playerTarget.AliveIn(session))));

    Assert.Equal(70, breakdown.FinalChance);
  }

  [TestCase(TestName = "GetHitChanceForAttack matches the breakdown the attack resolves with")]
  public void GetHitChanceForAttackMatchesTheBreakdownTheAttackResolvesWith()
  {
    var board = new BattleBoardState(new Vector3I(8, 1, 8));
    board.GetTile(board.At(4, 0, 4)).Cover = new TileCover(CoverDirections.North, 40);
    var battle = new BattleDuelBuilder
    {
      Board = board,
      RandomSeed = 99,
      Player = new DuelSide("Alpha", Weapon: BattleTestFactory.MakeWeapon("Rifle")),
    }.Start();

    var previewed = BattleQueryTestHelper.GetValue(
      BattleQueryTestHelper.Query(battle.Session, new GetHitChanceForAttack(
        battle.PlayerUnit.AliveIn(battle.Session), battle.EnemyUnit.AliveIn(battle.Session))));

    var raisedEvents = new List<BattleEvent>();
    battle.Session.BattleEventCommitted += raisedEvents.Add;
    battle.Executor.Submit(BattleAction.AttackUnit(
      battle.PlayerUnit.AliveIn(battle.Session),
      battle.EnemyUnit.AliveIn(battle.Session)));

    var resolved = raisedEvents.AsValueEnumerable().OfType<UnitAttackedBattleEvent>().Single().Breakdown;
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

    void AssertFails(AliveUnit attacker, AliveUnit attackTarget, BattleQueryFailureReason expectedReason)
    {
      var failure = BattleQueryTestHelper.GetFailure(
        BattleQueryTestHelper.Query(session, new GetHitChanceForAttack(attacker, attackTarget)));
      Assert.Equal(expectedReason, failure.Reason);
    }

    AssertFails(unarmedAttacker.AliveIn(session), target.AliveIn(session), BattleQueryFailureReason.InvalidBattleState);   // no weapon
    AssertFails(armedAttacker.AliveIn(session), armedAttacker.AliveIn(session), BattleQueryFailureReason.InvalidBattleState); // self-target
    AssertFails(armedAttacker.AliveIn(session), unarmedAttacker.AliveIn(session), BattleQueryFailureReason.InvalidBattleState); // allied target
    AssertFails(shortSightedAttacker.AliveIn(session), target.AliveIn(session), BattleQueryFailureReason.InvalidBattleState); // not visible
    AssertFails(shortRangedAttacker.AliveIn(session), target.AliveIn(session), BattleQueryFailureReason.InvalidBattleState);  // out of range
  }
}
