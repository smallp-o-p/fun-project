using System;
using System.Collections.Generic;
using FunProject.Battle;
using FunProject.Core;
using FunProject.Weapons;
using FunProject.Combatants;
using GdUnit4;
using Godot;

[TestSuite]
[RequireGodotRuntime]
public partial class AttackUnitTest
{
  [TestCase(TestName = "Guaranteed hit damages the target and raises attack then damage events")]
  public void GuaranteedHitDamagesTargetAndRaisesAttackThenDamageEvents()
  {
    using var battle = BattleFixture.Duel(
      player: new("Alpha", Aim: 100, Weapon: TestData.MakeWeapon("Rifle", damage: 5)));
    var attacker = battle.PlayerUnit;
    var target = battle.EnemyUnit;

    battle.ClearEvents();
    battle.Attack(attacker, target);

    battle.Read(() => Assert.Equal(target.MaxHealth - 5, target.CurrentHealth));
    battle.Read(() => Assert.Equal(attacker.MaxActionPoints - 1, attacker.CurrentActionPoints));

    var attackEvent = battle.Events.SingleEvent<UnitAttackedBattleEvent>();
    battle.Read(() => Assert.True(attackEvent.IsHit));
    battle.Read(() => Assert.Equal(100, attackEvent.Breakdown.FinalChance));
    battle.Read(() => Assert.True(attackEvent.Roll >= 0));
    battle.Read(() => Assert.True(attackEvent.Roll < 100));

    battle.Events.EventBefore<UnitAttackedBattleEvent, UnitDamagedBattleEvent>();
  }

  [TestCase(TestName = "Guaranteed miss spends action points without damage")]
  public void GuaranteedMissSpendsActionPointsWithoutDamage()
  {
    using var battle = BattleFixture.Duel(
      player: new("Alpha", Aim: 0, Weapon: TestData.MakeWeapon("Rifle")));
    var attacker = battle.PlayerUnit;
    var target = battle.EnemyUnit;

    battle.ClearEvents();
    battle.Attack(attacker, target);

    battle.Read(() => Assert.Equal(target.MaxHealth, target.CurrentHealth));
    battle.Read(() => Assert.Equal(attacker.MaxActionPoints - 1, attacker.CurrentActionPoints));
    battle.Read(() => Assert.False(battle.Events.SingleEvent<UnitAttackedBattleEvent>().IsHit));
    battle.Read(() => Assert.False(battle.Events.EventsOf<UnitDamagedBattleEvent>().AsValueEnumerable().Any()));
  }

  [TestCase(TestName = "Applicable cover lowers the resolved hit chance")]
  public void ApplicableCoverLowersTheResolvedHitChance()
  {
    List<(Vector3I Cell, BattleMapTileData Tile)> tiles = [];
    for (int x = 0; x < 8; x++)
      for (int z = 0; z < 8; z++)
      {
        var cell = new Vector3I(x, 0, z);
        tiles.Add((cell, cell == new Vector3I(4, 0, 4)
          ? new BattleMapTileData { Walkable = true, CoverDirections = CoverDirections.North, CoverAmount = 60 }
          : TestData.FloorTile()));
      }
    using var battle = BattleFixture.Duel(
      map: TestData.MakeMapData(new Vector3I(8, 1, 8), [.. tiles]),
      player: new("Alpha", Aim: 60, Weapon: TestData.MakeWeapon("Rifle")));

    battle.ClearEvents();
    battle.Attack(battle.PlayerUnit, battle.EnemyUnit);

    var attackEvent = battle.Events.SingleEvent<UnitAttackedBattleEvent>();
    battle.Read(() => Assert.Equal(0, attackEvent.Breakdown.FinalChance));
    battle.Read(() => Assert.False(attackEvent.IsHit));
    battle.Read(() => Assert.Equal(StandardHitChanceCalculator.CoverModifierLabel, attackEvent.Breakdown.Modifiers.AsValueEnumerable().Single().Label));
  }

  [TestCase(TestName = "Injected calculator replaces the hit chance algorithm")]
  public void InjectedCalculatorReplacesTheHitChanceAlgorithm()
  {
    using var battle = BattleFixture.Duel(
      hitChanceCalculator: new AlwaysHitCalculator(),
      player: new("Alpha", Aim: 0, Weapon: TestData.MakeWeapon("Rifle", damage: 5)));

    battle.Attack(battle.PlayerUnit, battle.EnemyUnit);

    battle.Read(() => Assert.Equal(battle.EnemyUnit.MaxHealth - 5, battle.EnemyUnit.CurrentHealth));
  }

  [TestCase(TestName = "Infeasible attacks throw without spending action points")]
  public void InfeasibleAttacksThrowWithoutSpendingActionPoints()
  {
    var playerFaction = TestData.MakeFaction("Player");
    var enemyFaction = TestData.MakeFaction("Enemy");
    using var battle = new BattleFixture(new BattleSetup(
      TestData.MakeOpenBattleMap(new Vector3I(8, 1, 8)),
      [
        new BattleSideSetup(playerFaction, [new FakeObjectiveData()],
          [
            new UnitPlacement(new UnitLoadout(TestData.MakeCombatant("Alpha", playerFaction, aim: 100),
              Some(TestData.MakeWeapon("Rifle"))), new Vector3I(4, 0, 1)),
            new UnitPlacement(new UnitLoadout(TestData.MakeCombatant("Bravo", playerFaction)), new Vector3I(5, 0, 1)),
            new UnitPlacement(new UnitLoadout(TestData.MakeCombatant("Charlie", playerFaction, vision: 1, aim: 100),
              Some(TestData.MakeWeapon("Rifle"))), new Vector3I(6, 0, 1)),
            new UnitPlacement(new UnitLoadout(TestData.MakeCombatant("Delta", playerFaction, aim: 100),
              Some(TestData.MakeWeapon("Pistol", range: 1))), new Vector3I(7, 0, 1)),
            new UnitPlacement(new UnitLoadout(TestData.MakeCombatant("Echo", playerFaction)), new Vector3I(3, 0, 1)),
          ]),
        new BattleSideSetup(enemyFaction, [new FakeObjectiveData()],
          [new UnitPlacement(new UnitLoadout(TestData.MakeCombatant("Hostile", enemyFaction)), new Vector3I(4, 0, 4))]),
      ],
      Seed: 7));
    var armedAttacker = battle.UnitAt(new Vector3I(4, 0, 1));
    var unarmedAttacker = battle.UnitAt(new Vector3I(5, 0, 1));
    var shortSightedAttacker = battle.UnitAt(new Vector3I(6, 0, 1));
    var shortRangedAttacker = battle.UnitAt(new Vector3I(7, 0, 1));
    var ally = battle.UnitAt(new Vector3I(3, 0, 1));
    var target = battle.UnitAt(new Vector3I(4, 0, 4));

    // Feasibility misses interrupt silently (staleness from interleaved interrupts cannot
    // be distinguished from caller bugs at Execute time): the attack drops, nothing is
    // spent, and the submission completes.
    void AssertInfeasible(Func<BattleAction> makeAttack, BattleUnitState unit)
    {
      int actionPointsBefore = battle.Read(() => unit.CurrentActionPoints);
      battle.Submit(makeAttack());
      battle.Read(() => Assert.Equal(actionPointsBefore, unit.CurrentActionPoints));
    }

    AssertInfeasible(
      () => BattleAction.AttackEntity(battle.Alive(unarmedAttacker), battle.Target(target)),
      unarmedAttacker);                            // no equipped weapon
    AssertInfeasible(
      () => BattleAction.AttackEntity(battle.Alive(armedAttacker), battle.Target(armedAttacker)),
      armedAttacker);                              // self-target
    AssertInfeasible(
      () => BattleAction.AttackEntity(battle.Alive(armedAttacker), battle.Target(ally)),
      armedAttacker);                              // allied target
    AssertInfeasible(
      () => BattleAction.AttackEntity(battle.Alive(shortSightedAttacker), battle.Target(target)),
      shortSightedAttacker);                       // not visible (vision 1, distance 3)
    AssertInfeasible(
      () => BattleAction.AttackEntity(battle.Alive(shortRangedAttacker), battle.Target(target)),
      shortRangedAttacker);                        // out of weapon range (range 1, distance > 1)

    battle.ApplyDamage(target, 999);
    // The dead-target case dies at the proof mint itself: TryGetAttackTarget refuses to mint
    // for a dead unit, so constructing the action (not executing it) throws.
    Assert.Throws<InvalidOperationException>(()
      => BattleAction.AttackEntity(battle.Alive(armedAttacker), battle.Session.TryGetAttackTarget(new BattleEntity.Unit(target)).RequireSome()));
  }

  [TestCase(TestName = "A stale attack whose target died to an earlier interrupt interrupts silently")]
  public void StaleAttackOnKilledTargetInterruptsSilently()
  {
    var weapon = TestData.MakeAmmoWeapon("Sentinel Rifle", magazine: 2, damage: 5);
    using var battle = BattleFixture.Duel(
      hitChanceCalculator: new AlwaysHitCalculator(),
      player: new("Alpha", Health: 5),
      enemy: new("Hostile", Aim: 100, Weapon: weapon));
    var mover = battle.PlayerUnit;
    var shooter = battle.EnemyUnit;

    // Both overwatch shots are built during the move event's dispatch, while the mover is
    // still alive; the first kill invalidates the second action's target proof.
    battle.RegisterHook<UnitMovedBattleEvent>(new BuildInterruptsHook(context =>
    [
      BattleAction.AttackEntity(
        context.Session.TryGetAlive(shooter).RequireSome(),
        context.Session.TryGetAttackTarget(new BattleEntity.Unit(mover)).RequireSome()),
      BattleAction.AttackEntity(
        context.Session.TryGetAlive(shooter).RequireSome(),
        context.Session.TryGetAttackTarget(new BattleEntity.Unit(mover)).RequireSome()),
    ]));
    battle.ClearEvents();

    Vector3I from = battle.Read(() => battle.Session.GetUnitPosition(mover).RequireSome().Raw);
    battle.Move(mover, [from + new Vector3I(0, 0, 1)]);

    battle.Read(() => Assert.True(mover.IsDead));
    battle.Read(() => Assert.Equal(1, battle.Events.EventsOf<UnitAttackedBattleEvent>().AsValueEnumerable().Count()));
    battle.Read(() => Assert.Equal(1, weapon.CurrentAmmo));
  }

  [TestCase(TestName = "Same seed produces the same attack outcome sequence")]
  public void SameSeedProducesTheSameAttackOutcomeSequence()
  {
    Assert.True(RunSeededAttackOutcomes(1234).AsValueEnumerable().SequenceEqual(RunSeededAttackOutcomes(1234)));
  }

  [TestCase(TestName = "Damage event carries the weapon's emitted bundle")]
  public void DamageEventCarriesEmittedBundle()
  {
    var weapon = TestData.MakeWeapon(
      "Plasma Pistol", damage: 6, frame: TestData.MakeFrame((Element.Thermal, 0.7f), (Element.Electrical, 0.3f)));
    using var battle = BattleFixture.Duel(
      hitChanceCalculator: new AlwaysHitCalculator(),
      player: new("Alpha", Aim: 100, Weapon: weapon));
    var target = battle.EnemyUnit;

    battle.ClearEvents();
    battle.Attack(battle.PlayerUnit, target);

    var damagedEvent = battle.Events.SingleEvent<UnitDamagedBattleEvent>();
    battle.Read(() => Assert.Equal(6, damagedEvent.TotalAmount)); // 4 Thermal + 2 Electrical
    battle.Read(() => Assert.Equal(target.MaxHealth - 6, target.CurrentHealth));
    battle.Read(() => Assert.True(damagedEvent.Bundle.AsValueEnumerable().SequenceEqual(
      new[] { new Damage(4, Element.Thermal), new Damage(2, Element.Electrical) })));
  }

  [TestCase(TestName = "Int damage path wraps the amount as a single Kinetic packet")]
  public void IntDamagePathWrapsAsKineticPacket()
  {
    using var solo = BattleFixture.Solo(new Vector3I(4, 1, 4), new Vector3I(0, 0, 0));

    solo.ClearEvents();
    solo.ApplyDamage(solo.Unit, 3);

    var damagedEvent = solo.Events.SingleEvent<UnitDamagedBattleEvent>();
    solo.Read(() => Assert.Equal(3, damagedEvent.TotalAmount));
    solo.Read(() => Assert.True(damagedEvent.Bundle.AsValueEnumerable().SequenceEqual(new[] { new Damage(3, Element.Kinetic) })));
  }

  [TestCase(TestName = "An empty magazine interrupts the attack without spending AP or raising events")]
  public void EmptyMagazineInterruptsAttack()
  {
    var weapon = TestData.MakeAmmoWeapon("Pistol", magazine: 1, damage: 2);
    using var battle = BattleFixture.Duel(
      hitChanceCalculator: new AlwaysHitCalculator(),
      player: new("Alpha", Weapon: weapon));
    var attacker = battle.PlayerUnit;
    var target = battle.EnemyUnit;

    battle.Attack(attacker, target);
    battle.Read(() => Assert.Equal(0, weapon.CurrentAmmo));

    battle.ClearEvents();
    int actionPointsBefore = battle.Read(() => attacker.CurrentActionPoints);

    battle.Attack(attacker, target);

    battle.Read(() => Assert.Equal(actionPointsBefore, attacker.CurrentActionPoints));
    battle.Read(() => Assert.False(battle.Events.EventsOf<UnitAttackedBattleEvent>().AsValueEnumerable().Any()));
  }

  [TestCase(TestName = "A missed shot still spends ammunition")]
  public void MissedShotSpendsAmmo()
  {
    var weapon = TestData.MakeAmmoWeapon("Pistol", magazine: 3);
    using var battle = BattleFixture.Duel(
      player: new("Alpha", Aim: 0, Weapon: weapon));
    var target = battle.EnemyUnit;

    battle.Attack(battle.PlayerUnit, target);

    battle.Read(() => Assert.Equal(target.MaxHealth, target.CurrentHealth)); // aim 0 -> guaranteed miss
    battle.Read(() => Assert.Equal(2, weapon.CurrentAmmo));
  }

  private static List<bool> RunSeededAttackOutcomes(int seed)
  {
    using var battle = BattleFixture.Duel(
      randomSeed: seed,
      player: new("Alpha", ActionPoints: 5, Aim: 50, Weapon: TestData.MakeWeapon("Rifle")),
      enemy: new("Hostile", Health: 100));

    battle.ClearEvents();
    for (int i = 0; i < 5; i++)
      battle.Attack(battle.PlayerUnit, battle.EnemyUnit);

    return battle.Events.EventsOf<UnitAttackedBattleEvent>().AsValueEnumerable().Select(attackEvent => attackEvent.IsHit).ToList();
  }

  private sealed class BuildInterruptsHook(Func<HookContext, IReadOnlyList<BattleAction>> build) : BattleHook<UnitMovedBattleEvent>
  {
    protected override IReadOnlyList<BattleAction> OnEvent(HookContext context, UnitMovedBattleEvent evt)
      => build(context);
  }
}
