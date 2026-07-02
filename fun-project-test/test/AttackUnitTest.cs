using FunProject.Battle;
using FunProject.Core;
using FunProject.Weapons;
using GdUnit4;
using Godot;
using System.Collections.Generic;
using System.Linq;

[TestSuite]
[RequireGodotRuntime]
public partial class AttackUnitTest
{
  [TestCase(TestName = "Guaranteed hit damages the target and raises attack then damage events")]
  public void GuaranteedHitDamagesTargetAndRaisesAttackThenDamageEvents()
  {
    var battle = new BattleDuelBuilder
    {
      Player = new DuelSide("Alpha", Aim: 100, Weapon: BattleTestFactory.MakeWeapon("Rifle", damage: 5)),
    }.Start();
    var attacker = battle.PlayerUnit;
    var target = battle.EnemyUnit;

    var recorder = new BattleEventRecorder(battle.Session);
    var result = battle.Executor.Submit(BattleAction.AttackUnit(attacker.State, target.State)).RequireSingleResult();

    Assert.True(result.Succeeded);
    Assert.Equal(target.State.MaxHealth - 5, target.State.CurrentHealth);
    Assert.Equal(attacker.State.MaxActionPoints - 1, attacker.CurrentActionPoints);

    var attackEvent = recorder.Single<UnitAttackedBattleEvent>();
    Assert.True(attackEvent.IsHit);
    Assert.Equal(100, attackEvent.Breakdown.FinalChance);
    Assert.True(attackEvent.Roll >= 0);
    Assert.True(attackEvent.Roll < 100);

    recorder.AssertCommittedBefore<UnitAttackedBattleEvent, UnitDamagedBattleEvent>();
  }

  [TestCase(TestName = "Guaranteed miss spends action points without damage")]
  public void GuaranteedMissSpendsActionPointsWithoutDamage()
  {
    var battle = new BattleDuelBuilder
    {
      Player = new DuelSide("Alpha", Aim: 0, Weapon: BattleTestFactory.MakeWeapon("Rifle")),
    }.Start();
    var attacker = battle.PlayerUnit;
    var target = battle.EnemyUnit;

    var recorder = new BattleEventRecorder(battle.Session);
    var result = battle.Executor.Submit(BattleAction.AttackUnit(attacker.State, target.State)).RequireSingleResult();

    Assert.True(result.Succeeded);
    Assert.Equal(target.State.MaxHealth, target.State.CurrentHealth);
    Assert.Equal(attacker.State.MaxActionPoints - 1, attacker.CurrentActionPoints);
    Assert.False(recorder.Single<UnitAttackedBattleEvent>().IsHit);
    Assert.False(recorder.OfType<UnitDamagedBattleEvent>().Any());
  }

  [TestCase(TestName = "Applicable cover lowers the resolved hit chance")]
  public void ApplicableCoverLowersTheResolvedHitChance()
  {
    var board = new BattleBoardState(new Vector3I(8, 1, 8));
    board.GetTile(board.At(4, 0, 4)).Cover = new TileCover(CoverDirections.North, 60);
    var battle = new BattleDuelBuilder
    {
      Board = board,
      Player = new DuelSide("Alpha", Aim: 60, Weapon: BattleTestFactory.MakeWeapon("Rifle")),
    }.Start();

    var recorder = new BattleEventRecorder(battle.Session);
    var result = battle.Executor.Submit(BattleAction.AttackUnit(battle.PlayerUnit.State, battle.EnemyUnit.State)).RequireSingleResult();

    Assert.True(result.Succeeded);
    var attackEvent = recorder.Single<UnitAttackedBattleEvent>();
    Assert.Equal(0, attackEvent.Breakdown.FinalChance);
    Assert.False(attackEvent.IsHit);
    Assert.Equal(StandardHitChanceCalculator.CoverModifierLabel, attackEvent.Breakdown.Modifiers.Single().Label);
  }

  [TestCase(TestName = "Injected calculator replaces the hit chance algorithm")]
  public void InjectedCalculatorReplacesTheHitChanceAlgorithm()
  {
    var battle = new BattleDuelBuilder
    {
      HitChanceCalculator = new AlwaysHitCalculator(),
      Player = new DuelSide("Alpha", Aim: 0, Weapon: BattleTestFactory.MakeWeapon("Rifle", damage: 5)),
    }.Start();

    var result = battle.Executor.Submit(BattleAction.AttackUnit(battle.PlayerUnit.State, battle.EnemyUnit.State)).RequireSingleResult();

    Assert.True(result.Succeeded);
    Assert.Equal(battle.EnemyUnit.State.MaxHealth - 5, battle.EnemyUnit.State.CurrentHealth);
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

  [TestCase(TestName = "Damage event carries the weapon's emitted bundle")]
  public void DamageEventCarriesEmittedBundle()
  {
    var weapon = BattleTestFactory.MakeWeapon(
      "Plasma Pistol", damage: 6, frame: BattleTestFactory.MakeFrame((Element.Thermal, 0.7f), (Element.Electrical, 0.3f)));
    var battle = new BattleDuelBuilder
    {
      HitChanceCalculator = new AlwaysHitCalculator(),
      Player = new DuelSide("Alpha", Aim: 100, Weapon: weapon),
    }.Start();
    var target = battle.EnemyUnit;

    var recorder = new BattleEventRecorder(battle.Session);
    Assert.True(battle.Executor.Submit(BattleAction.AttackUnit(battle.PlayerUnit.State, target.State)).RequireSingleResult().Succeeded);

    var damagedEvent = recorder.Single<UnitDamagedBattleEvent>();
    Assert.Equal(6, damagedEvent.TotalAmount); // 4 Thermal + 2 Electrical
    Assert.Equal(target.State.MaxHealth - 6, target.State.CurrentHealth);
    Assert.True(damagedEvent.Bundle.SequenceEqual(
      new[] { new Damage(4, Element.Thermal), new Damage(2, Element.Electrical) }));
  }

  [TestCase(TestName = "Int damage path wraps the amount as a single Kinetic packet")]
  public void IntDamagePathWrapsAsKineticPacket()
  {
    var solo = StartSoloBattle(new Vector3I(4, 1, 4), new Vector3I(0, 0, 0));

    var recorder = new BattleEventRecorder(solo.Session);
    Assert.True(solo.Executor.Submit(BattleAction.ApplyDamage(solo.Unit.State, 3)).RequireSingleResult().Succeeded);

    var damagedEvent = recorder.Single<UnitDamagedBattleEvent>();
    Assert.Equal(3, damagedEvent.TotalAmount);
    Assert.True(damagedEvent.Bundle.SequenceEqual(new[] { new Damage(3, Element.Kinetic) }));
  }

  [TestCase(TestName = "An empty magazine rejects the attack without spending AP or raising events")]
  public void EmptyMagazineRejectsAttack()
  {
    var weapon = BattleTestFactory.MakeAmmoWeapon("Pistol", magazine: 1, damage: 2);
    var battle = new BattleDuelBuilder
    {
      HitChanceCalculator = new AlwaysHitCalculator(),
      Player = new DuelSide("Alpha", Weapon: weapon),
    }.Start();
    var attacker = battle.PlayerUnit;
    var target = battle.EnemyUnit;

    Assert.True(battle.Executor.Submit(BattleAction.AttackUnit(attacker.State, target.State)).RequireSingleResult().Succeeded);
    Assert.Equal(0, weapon.CurrentAmmo);

    var recorder = new BattleEventRecorder(battle.Session);
    int actionPointsBefore = attacker.CurrentActionPoints;

    var result = battle.Executor.Submit(BattleAction.AttackUnit(attacker.State, target.State)).RequireSingleResult();

    Assert.False(result.Succeeded);
    Assert.Equal(BattleActionFailureReason.Rejected, result.FailureReason);
    Assert.Equal(actionPointsBefore, attacker.CurrentActionPoints);
    Assert.False(recorder.OfType<UnitAttackedBattleEvent>().Any());
  }

  [TestCase(TestName = "A missed shot still spends ammunition")]
  public void MissedShotSpendsAmmo()
  {
    var weapon = BattleTestFactory.MakeAmmoWeapon("Pistol", magazine: 3);
    var battle = new BattleDuelBuilder
    {
      Player = new DuelSide("Alpha", Aim: 0, Weapon: weapon),
    }.Start();
    var target = battle.EnemyUnit;

    var result = battle.Executor.Submit(BattleAction.AttackUnit(battle.PlayerUnit.State, target.State)).RequireSingleResult();

    Assert.True(result.Succeeded);
    Assert.Equal(target.State.MaxHealth, target.State.CurrentHealth); // aim 0 -> guaranteed miss
    Assert.Equal(2, weapon.CurrentAmmo);
  }

  private static List<bool> RunSeededAttackOutcomes(int seed)
  {
    var battle = new BattleDuelBuilder
    {
      RandomSeed = seed,
      Player = new DuelSide("Alpha", ActionPoints: 5, Aim: 50, Weapon: BattleTestFactory.MakeWeapon("Rifle")),
      Enemy = new DuelSide("Hostile", Health: 100),
    }.Start();

    var recorder = new BattleEventRecorder(battle.Session);
    for (int i = 0; i < 5; i++)
      Assert.True(battle.Executor.Submit(BattleAction.AttackUnit(battle.PlayerUnit.State, battle.EnemyUnit.State)).RequireSingleResult().Succeeded);

    return recorder.OfType<UnitAttackedBattleEvent>().Select(attackEvent => attackEvent.IsHit).ToList();
  }
}
