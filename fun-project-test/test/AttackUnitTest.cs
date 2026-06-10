using FunProject.Battle;
using FunProject.Tests;
using GdUnit4;
using Godot;
using System.Collections.Generic;
using System.Linq;
using static BattleActionTestHelper;

[TestSuite]
[RequireGodotRuntime]
public partial class AttackUnitTest
{
  private sealed class AlwaysHitCalculator : IHitChanceCalculator
  {
    public HitChanceBreakdown Calculate(AttackContext context)
    {
      return new HitChanceBreakdown(100, []);
    }
  }

  [TestCase(TestName = "Guaranteed hit damages the target and raises attack then damage events")]
  public void GuaranteedHitDamagesTargetAndRaisesAttackThenDamageEvents()
  {
    var playerFaction = BattleTestFactory.MakeFaction("Player");
    var enemyFaction = BattleTestFactory.MakeFaction("Enemy");
    var session = BattleTestFactory.MakeSession(new Vector3I(8, 1, 8), [playerFaction, enemyFaction]);
    var attacker = SpawnUnit(session, BattleTestFactory.MakeCombatant("Alpha", playerFaction, aim: 100), new Vector3I(4, 0, 1), BattleTestFactory.MakeWeapon("Rifle", damage: 5));
    var target = SpawnUnit(session, BattleTestFactory.MakeCombatant("Hostile", enemyFaction), new Vector3I(4, 0, 4));
    StartBattle(session);

    var raisedEvents = new List<BattleEvent>();
    session.BattleEventCommitted += raisedEvents.Add;

    var executor = new BattleActionExecutor(session);
    var result = executor.Submit(BattleAction.AttackUnit(attacker.State, target.State)).RequireSingleResult();

    Assert.True(result.Succeeded);
    Assert.Equal(target.State.MaxHealth - 5, target.State.CurrentHealth);
    Assert.Equal(attacker.State.MaxActionPoints - 1, attacker.CurrentActionPoints);

    var attackEvent = raisedEvents.OfType<UnitAttackedBattleEvent>().Single();
    Assert.True(attackEvent.IsHit);
    Assert.Equal(100, attackEvent.Breakdown.FinalChance);
    Assert.True(attackEvent.Roll >= 0);
    Assert.True(attackEvent.Roll < 100);

    int attackedIndex = raisedEvents.FindIndex(battleEvent => battleEvent is UnitAttackedBattleEvent);
    int damagedIndex = raisedEvents.FindIndex(battleEvent => battleEvent is UnitDamagedBattleEvent);
    Assert.True(attackedIndex >= 0);
    Assert.True(damagedIndex > attackedIndex);
  }

  [TestCase(TestName = "Guaranteed miss spends action points without damage")]
  public void GuaranteedMissSpendsActionPointsWithoutDamage()
  {
    var playerFaction = BattleTestFactory.MakeFaction("Player");
    var enemyFaction = BattleTestFactory.MakeFaction("Enemy");
    var session = BattleTestFactory.MakeSession(new Vector3I(8, 1, 8), [playerFaction, enemyFaction]);
    var attacker = SpawnUnit(session, BattleTestFactory.MakeCombatant("Alpha", playerFaction, aim: 0), new Vector3I(4, 0, 1), BattleTestFactory.MakeWeapon("Rifle"));
    var target = SpawnUnit(session, BattleTestFactory.MakeCombatant("Hostile", enemyFaction), new Vector3I(4, 0, 4));
    StartBattle(session);

    var raisedEvents = new List<BattleEvent>();
    session.BattleEventCommitted += raisedEvents.Add;

    var executor = new BattleActionExecutor(session);
    var result = executor.Submit(BattleAction.AttackUnit(attacker.State, target.State)).RequireSingleResult();

    Assert.True(result.Succeeded);
    Assert.Equal(target.State.MaxHealth, target.State.CurrentHealth);
    Assert.Equal(attacker.State.MaxActionPoints - 1, attacker.CurrentActionPoints);
    Assert.False(raisedEvents.OfType<UnitAttackedBattleEvent>().Single().IsHit);
    Assert.False(raisedEvents.OfType<UnitDamagedBattleEvent>().Any());
  }

  [TestCase(TestName = "Applicable cover lowers the resolved hit chance")]
  public void ApplicableCoverLowersTheResolvedHitChance()
  {
    var playerFaction = BattleTestFactory.MakeFaction("Player");
    var enemyFaction = BattleTestFactory.MakeFaction("Enemy");
    var board = new BattleBoardState(new Vector3I(8, 1, 8));
    var targetPoint = board.ValidatePoint(new Vector3I(4, 0, 4)).RequireSome();
    board.GetTile(targetPoint).Cover = new TileCover(CoverDirections.North, 60);
    var session = BattleTestFactory.MakeSession(board, [playerFaction, enemyFaction]);
    var attacker = SpawnUnit(session, BattleTestFactory.MakeCombatant("Alpha", playerFaction, aim: 60), new Vector3I(4, 0, 1), BattleTestFactory.MakeWeapon("Rifle"));
    var target = SpawnUnit(session, BattleTestFactory.MakeCombatant("Hostile", enemyFaction), new Vector3I(4, 0, 4));
    StartBattle(session);

    var raisedEvents = new List<BattleEvent>();
    session.BattleEventCommitted += raisedEvents.Add;

    var executor = new BattleActionExecutor(session);
    var result = executor.Submit(BattleAction.AttackUnit(attacker.State, target.State)).RequireSingleResult();

    Assert.True(result.Succeeded);
    var attackEvent = raisedEvents.OfType<UnitAttackedBattleEvent>().Single();
    Assert.Equal(0, attackEvent.Breakdown.FinalChance);
    Assert.False(attackEvent.IsHit);
    Assert.Equal(StandardHitChanceCalculator.CoverModifierLabel, attackEvent.Breakdown.Modifiers.Single().Label);
  }

  [TestCase(TestName = "Injected calculator replaces the hit chance algorithm")]
  public void InjectedCalculatorReplacesTheHitChanceAlgorithm()
  {
    var playerFaction = BattleTestFactory.MakeFaction("Player");
    var enemyFaction = BattleTestFactory.MakeFaction("Enemy");
    var session = BattleTestFactory.MakeSession(new Vector3I(8, 1, 8), [playerFaction, enemyFaction], new AlwaysHitCalculator());
    var attacker = SpawnUnit(session, BattleTestFactory.MakeCombatant("Alpha", playerFaction, aim: 0), new Vector3I(4, 0, 1), BattleTestFactory.MakeWeapon("Rifle", damage: 5));
    var target = SpawnUnit(session, BattleTestFactory.MakeCombatant("Hostile", enemyFaction), new Vector3I(4, 0, 4));
    StartBattle(session);

    var executor = new BattleActionExecutor(session);
    var result = executor.Submit(BattleAction.AttackUnit(attacker.State, target.State)).RequireSingleResult();

    Assert.True(result.Succeeded);
    Assert.Equal(target.State.MaxHealth - 5, target.State.CurrentHealth);
  }

  [TestCase(TestName = "Attack rejections leave action points unspent")]
  public void AttackRejectionsLeaveActionPointsUnspent()
  {
    var playerFaction = BattleTestFactory.MakeFaction("Player");
    var enemyFaction = BattleTestFactory.MakeFaction("Enemy");
    var session = BattleTestFactory.MakeSession(new Vector3I(8, 1, 8), [playerFaction, enemyFaction]);
    var armedAttacker = SpawnUnit(session, BattleTestFactory.MakeCombatant("Alpha", playerFaction, aim: 100), new Vector3I(4, 0, 1), BattleTestFactory.MakeWeapon("Rifle"));
    var unarmedAttacker = SpawnUnit(session, BattleTestFactory.MakeCombatant("Bravo", playerFaction), new Vector3I(5, 0, 1));
    var shortSightedAttacker = SpawnUnit(session, BattleTestFactory.MakeCombatant("Charlie", playerFaction, vision: 1, aim: 100), new Vector3I(6, 0, 1), BattleTestFactory.MakeWeapon("Rifle"));
    var shortRangedAttacker = SpawnUnit(session, BattleTestFactory.MakeCombatant("Delta", playerFaction, aim: 100), new Vector3I(7, 0, 1), BattleTestFactory.MakeWeapon("Pistol", range: 1));
    var ally = SpawnUnit(session, BattleTestFactory.MakeCombatant("Echo", playerFaction), new Vector3I(3, 0, 1));
    var target = SpawnUnit(session, BattleTestFactory.MakeCombatant("Hostile", enemyFaction), new Vector3I(4, 0, 4));
    StartBattle(session);

    var executor = new BattleActionExecutor(session);

    void AssertRejected(BattleTestUnit unit, BattleUnitState attackTarget)
    {
      int actionPointsBefore = unit.CurrentActionPoints;
      var result = executor.Submit(BattleAction.AttackUnit(unit.State, attackTarget)).RequireSingleResult();
      Assert.False(result.Succeeded);
      Assert.Equal(BattleActionFailureReason.Rejected, result.FailureReason);
      Assert.Equal(actionPointsBefore, unit.CurrentActionPoints);
    }

    AssertRejected(unarmedAttacker, target.State);          // no equipped weapon
    AssertRejected(armedAttacker, armedAttacker.State);     // self-target
    AssertRejected(armedAttacker, ally.State);              // allied target
    AssertRejected(shortSightedAttacker, target.State);     // not visible (vision 1, distance 3)
    AssertRejected(shortRangedAttacker, target.State);      // out of weapon range (range 1, distance > 1)

    executor.Submit(BattleAction.ApplyDamage(target.State, 999));
    AssertRejected(armedAttacker, target.State);            // dead target
  }

  [TestCase(TestName = "Same seed produces the same attack outcome sequence")]
  public void SameSeedProducesTheSameAttackOutcomeSequence()
  {
    Assert.True(RunSeededAttackOutcomes(1234).SequenceEqual(RunSeededAttackOutcomes(1234)));
  }

  private static List<bool> RunSeededAttackOutcomes(int seed)
  {
    var playerFaction = BattleTestFactory.MakeFaction("Player");
    var enemyFaction = BattleTestFactory.MakeFaction("Enemy");
    var session = BattleTestFactory.MakeSession(new Vector3I(8, 1, 8), [playerFaction, enemyFaction], randomSeed: seed);
    var attacker = SpawnUnit(session, BattleTestFactory.MakeCombatant("Alpha", playerFaction, actionPoints: 5, aim: 50), new Vector3I(4, 0, 1), BattleTestFactory.MakeWeapon("Rifle"));
    var target = SpawnUnit(session, BattleTestFactory.MakeCombatant("Hostile", enemyFaction, health: 100), new Vector3I(4, 0, 4));
    StartBattle(session);

    var outcomes = new List<bool>();
    session.BattleEventCommitted += battleEvent =>
    {
      if (battleEvent is UnitAttackedBattleEvent attackEvent)
        outcomes.Add(attackEvent.IsHit);
    };

    var executor = new BattleActionExecutor(session);
    for (int i = 0; i < 5; i++)
      Assert.True(executor.Submit(BattleAction.AttackUnit(attacker.State, target.State)).RequireSingleResult().Succeeded);

    return outcomes;
  }
}
