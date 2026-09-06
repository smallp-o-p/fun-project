using FunProject.Battle;
using GdUnit4;
using Godot;

[TestSuite]
[RequireGodotRuntime]
public partial class BattleCombatQueriesTest
{
  [TestCase(TestName = "GetHitChanceForAttack returns the cover-modified breakdown")]
  public void GetHitChanceForAttackReturnsTheCoverModifiedBreakdown()
  {
    var board = new BattleBoardState(new Vector3I(8, 1, 8));
    board.GetTile(board.At(4, 0, 4)).Cover = new TileCover(CoverDirections.North, 40);
    using var battle = BattleFixture.Duel(
      board: board,
      player: new("Alpha", Weapon: TestData.MakeWeapon("Rifle")));

    var breakdown = battle.Query(new GetHitChanceForAttack(
      battle.Alive(battle.PlayerUnit), battle.Alive(battle.EnemyUnit))).RequireRight();

    Assert.Equal(65, breakdown.BaseChance);
    Assert.Equal(-40, breakdown.Modifiers.AsValueEnumerable().Single().Amount);
    Assert.Equal(25, breakdown.FinalChance);
  }

  [TestCase(TestName = "GetHitChanceForAttack ignores turn order")]
  public void GetHitChanceForAttackIgnoresTurnOrder()
  {
    var playerFaction = TestData.MakeFaction("Player");
    var enemyFaction = TestData.MakeFaction("Enemy");
    using var battle = new BattleFixture(new Vector3I(8, 1, 8), [playerFaction, enemyFaction]);
    battle.Spawn(TestData.MakeCombatant("Alpha", playerFaction), new Vector3I(4, 0, 1));
    var enemyAttacker = battle.Spawn(TestData.MakeCombatant("Hostile", enemyFaction, aim: 70), new Vector3I(4, 0, 4), TestData.MakeWeapon("Rifle"));
    var playerTarget = battle.Spawn(TestData.MakeCombatant("Bravo", playerFaction), new Vector3I(4, 0, 6));
    battle.Start();

    // Player faction is active; the enemy attacker can still preview its odds.
    var breakdown = battle.Query(new GetHitChanceForAttack(
      battle.Alive(enemyAttacker), battle.Alive(playerTarget))).RequireRight();

    Assert.Equal(70, breakdown.FinalChance);
  }

  [TestCase(TestName = "GetHitChanceForAttack matches the breakdown the attack resolves with")]
  public void GetHitChanceForAttackMatchesTheBreakdownTheAttackResolvesWith()
  {
    var board = new BattleBoardState(new Vector3I(8, 1, 8));
    board.GetTile(board.At(4, 0, 4)).Cover = new TileCover(CoverDirections.North, 40);
    using var battle = BattleFixture.Duel(
      board: board,
      randomSeed: 99,
      player: new("Alpha", Weapon: TestData.MakeWeapon("Rifle")));

    var previewed = battle.Query(new GetHitChanceForAttack(
      battle.Alive(battle.PlayerUnit), battle.Alive(battle.EnemyUnit))).RequireRight();

    battle.ClearEvents();
    battle.Attack(battle.PlayerUnit, battle.EnemyUnit);

    var resolved = battle.Events.SingleEvent<UnitAttackedBattleEvent>().Breakdown;
    Assert.Equal(previewed.BaseChance, resolved.BaseChance);
    Assert.Equal(previewed.FinalChance, resolved.FinalChance);
    Assert.Equal(previewed.Modifiers.Count, resolved.Modifiers.Count);
  }

  [TestCase(TestName = "GetHitChanceForAttack fails when the odds are undefined")]
  public void GetHitChanceForAttackFailsWhenTheOddsAreUndefined()
  {
    var playerFaction = TestData.MakeFaction("Player");
    var enemyFaction = TestData.MakeFaction("Enemy");
    using var battle = new BattleFixture(new Vector3I(8, 1, 8), [playerFaction, enemyFaction]);
    var armedAttacker = battle.Spawn(TestData.MakeCombatant("Alpha", playerFaction), new Vector3I(4, 0, 1), TestData.MakeWeapon("Rifle"));
    var unarmedAttacker = battle.Spawn(TestData.MakeCombatant("Bravo", playerFaction), new Vector3I(5, 0, 1));
    var shortSightedAttacker = battle.Spawn(TestData.MakeCombatant("Charlie", playerFaction, vision: 1), new Vector3I(6, 0, 1), TestData.MakeWeapon("Rifle"));
    var shortRangedAttacker = battle.Spawn(TestData.MakeCombatant("Delta", playerFaction), new Vector3I(7, 0, 1), TestData.MakeWeapon("Pistol", range: 1));
    var target = battle.Spawn(TestData.MakeCombatant("Hostile", enemyFaction), new Vector3I(4, 0, 4));
    battle.Start();

    void AssertFails(AliveUnit attacker, AliveUnit attackTarget, BattleQueryFailureReason expectedReason)
    {
      var failure = battle.Query(new GetHitChanceForAttack(attacker, attackTarget)).RequireLeft();
      Assert.Equal(expectedReason, failure.Reason);
    }

    AssertFails(battle.Alive(unarmedAttacker), battle.Alive(target), BattleQueryFailureReason.InvalidBattleState);   // no weapon
    AssertFails(battle.Alive(armedAttacker), battle.Alive(armedAttacker), BattleQueryFailureReason.InvalidBattleState); // self-target
    AssertFails(battle.Alive(armedAttacker), battle.Alive(unarmedAttacker), BattleQueryFailureReason.InvalidBattleState); // allied target
    AssertFails(battle.Alive(shortSightedAttacker), battle.Alive(target), BattleQueryFailureReason.InvalidBattleState); // not visible
    AssertFails(battle.Alive(shortRangedAttacker), battle.Alive(target), BattleQueryFailureReason.InvalidBattleState);  // out of range
  }
}
