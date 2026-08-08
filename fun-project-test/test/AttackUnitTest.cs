using FunProject.Battle;
using FunProject.Core;
using FunProject.Weapons;
using GdUnit4;
using Godot;
using System;
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
    Attack(battle.Session, battle.Executor, attacker.State, target.State);

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
    Attack(battle.Session, battle.Executor, attacker.State, target.State);

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
    Attack(battle.Session, battle.Executor, battle.PlayerUnit.State, battle.EnemyUnit.State);

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

    Attack(battle.Session, battle.Executor, battle.PlayerUnit.State, battle.EnemyUnit.State);

    Assert.Equal(battle.EnemyUnit.State.MaxHealth - 5, battle.EnemyUnit.State.CurrentHealth);
  }

  [TestCase(TestName = "Infeasible attacks throw without spending action points")]
  public void InfeasibleAttacksThrowWithoutSpendingActionPoints()
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

    var executor = ExecutorFor(session);

    // Feasibility misses interrupt silently (staleness from interleaved interrupts cannot
    // be distinguished from caller bugs at Execute time): the attack drops, nothing is
    // spent, and the submission completes.
    void AssertInfeasible(Func<BattleAction> makeAttack, BattleTestUnit unit)
    {
      int actionPointsBefore = unit.CurrentActionPoints;
      executor.Submit(makeAttack());
      Assert.Equal(actionPointsBefore, unit.CurrentActionPoints);
    }

    AssertInfeasible(
      () => BattleAction.AttackUnit(unarmedAttacker.AliveIn(session), target.AliveIn(session)),
      unarmedAttacker);                            // no equipped weapon
    AssertInfeasible(
      () => BattleAction.AttackUnit(armedAttacker.AliveIn(session), armedAttacker.AliveIn(session)),
      armedAttacker);                              // self-target
    AssertInfeasible(
      () => BattleAction.AttackUnit(armedAttacker.AliveIn(session), ally.AliveIn(session)),
      armedAttacker);                              // allied target
    AssertInfeasible(
      () => BattleAction.AttackUnit(shortSightedAttacker.AliveIn(session), target.AliveIn(session)),
      shortSightedAttacker);                       // not visible (vision 1, distance 3)
    AssertInfeasible(
      () => BattleAction.AttackUnit(shortRangedAttacker.AliveIn(session), target.AliveIn(session)),
      shortRangedAttacker);                        // out of weapon range (range 1, distance > 1)

    ApplyDamage(session, target.State, 999);
    // The dead-target case dies at the proof mint itself: TryGetAlive refuses to mint for a
    // dead unit, so constructing the action (not executing it) throws.
    Assert.Throws<InvalidOperationException>(()
      => BattleAction.AttackUnit(armedAttacker.AliveIn(session), session.TryGetAlive(target.State).RequireSome()));
  }

  [TestCase(TestName = "A stale attack whose target died to an earlier interrupt interrupts silently")]
  public void StaleAttackOnKilledTargetInterruptsSilently()
  {
    var weapon = BattleTestFactory.MakeAmmoWeapon("Sentinel Rifle", magazine: 2, damage: 5);
    var battle = new BattleDuelBuilder
    {
      HitChanceCalculator = new AlwaysHitCalculator(),
      Player = new DuelSide("Alpha", Health: 5),
      Enemy = new DuelSide("Hostile", Aim: 100, Weapon: weapon),
    }.Start();
    var mover = battle.PlayerUnit;
    var shooter = battle.EnemyUnit;

    // Both overwatch shots are built during the move event's dispatch, while the mover is
    // still alive; the first kill invalidates the second action's target proof.
    battle.Executor.RegisterHook<UnitMovedBattleEvent>(new BuildInterruptsHook(context =>
    [
      BattleAction.AttackUnit(
        context.Session.TryGetAlive(shooter.State).RequireSome(),
        context.Session.TryGetAlive(mover.State).RequireSome()),
      BattleAction.AttackUnit(
        context.Session.TryGetAlive(shooter.State).RequireSome(),
        context.Session.TryGetAlive(mover.State).RequireSome()),
    ]));
    var recorder = new BattleEventRecorder(battle.Session);

    Vector3I from = battle.Session.GetUnitPosition(mover.State).RequireSome().Raw;
    battle.Executor.Submit(BattleAction.MoveUnit(mover.AliveIn(battle.Session), [battle.Session.Board.At(from + new Vector3I(0, 0, 1))]));

    Assert.True(mover.State.IsDead);
    Assert.Equal(1, recorder.OfType<UnitAttackedBattleEvent>().Count());
    Assert.Equal(1, weapon.CurrentAmmo);
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
    Attack(battle.Session, battle.Executor, battle.PlayerUnit.State, target.State);

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
    solo.Executor.Submit(BattleAction.ApplyDamage(solo.Unit.AliveIn(solo.Session), 3));

    var damagedEvent = recorder.Single<UnitDamagedBattleEvent>();
    Assert.Equal(3, damagedEvent.TotalAmount);
    Assert.True(damagedEvent.Bundle.SequenceEqual(new[] { new Damage(3, Element.Kinetic) }));
  }

  [TestCase(TestName = "An empty magazine interrupts the attack without spending AP or raising events")]
  public void EmptyMagazineInterruptsAttack()
  {
    var weapon = BattleTestFactory.MakeAmmoWeapon("Pistol", magazine: 1, damage: 2);
    var battle = new BattleDuelBuilder
    {
      HitChanceCalculator = new AlwaysHitCalculator(),
      Player = new DuelSide("Alpha", Weapon: weapon),
    }.Start();
    var attacker = battle.PlayerUnit;
    var target = battle.EnemyUnit;

    Attack(battle.Session, battle.Executor, attacker.State, target.State);
    Assert.Equal(0, weapon.CurrentAmmo);

    var recorder = new BattleEventRecorder(battle.Session);
    int actionPointsBefore = attacker.CurrentActionPoints;

    battle.Executor.Submit(BattleAction.AttackUnit(attacker.AliveIn(battle.Session), target.AliveIn(battle.Session)));

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

    Attack(battle.Session, battle.Executor, battle.PlayerUnit.State, target.State);

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
      Attack(battle.Session, battle.Executor, battle.PlayerUnit.State, battle.EnemyUnit.State);

    return recorder.OfType<UnitAttackedBattleEvent>().Select(attackEvent => attackEvent.IsHit).ToList();
  }

  private sealed class BuildInterruptsHook(Func<HookContext, IReadOnlyList<BattleAction>> build) : BattleHook<UnitMovedBattleEvent>
  {
    protected override IReadOnlyList<BattleAction> OnEvent(HookContext context, UnitMovedBattleEvent evt)
      => build(context);
  }
}
