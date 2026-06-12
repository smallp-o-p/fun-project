using FunProject.Battle;
using FunProject.Core;
using FunProject.Items;
using FunProject.Items.Capabilities;
using FunProject.Items.Effects;
using FunProject.Stats;
using FunProject.Tests;
using FunProject.Weapons;
using GdUnit4;
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using static BattleActionTestHelper;

[TestSuite]
[RequireGodotRuntime]
public partial class StatusEffectBattleTest
{
  private sealed record Fixture(
    BattleSession Session,
    BattleActionExecutor Executor,
    FunProject.Combatants.Faction PlayerFaction,
    FunProject.Combatants.Faction EnemyFaction,
    BattleUnitState Attacker,
    BattleUnitState Target);

  private static Weapon MakeStatusWeapon(StatusEffectSpecData status, int damage = 3, Element element = Element.Kinetic)
  {
    var frame = new WeaponFrameData { Name = "Status Frame", Packets = [] };
    frame.Packets.Add(new DamagePacketData { Element = element, Multiplier = 1f, Status = status });
    return new Weapon(new WeaponData
    {
      Name = "Status Weapon",
      Description = "Applies a status on hit",
      Frame = frame,
      DamageStat = new DamageStat { BaseValue = damage },
      RangeStat = new RangeStat { BaseValue = 10 },
      CriticalChanceStat = new CriticalChanceStat { BaseValue = 0 },
    });
  }

  private static Fixture MakeFixture(
    Weapon attackerWeapon,
    ItemWith<ArmorCapability>? targetArmor = null,
    int targetHealth = 20,
    int? randomSeed = null)
  {
    var playerFaction = BattleTestFactory.MakeFaction("Player");
    var enemyFaction = BattleTestFactory.MakeFaction("Enemy");
    var session = BattleTestFactory.MakeSession(
      new Vector3I(8, 1, 8), [playerFaction, enemyFaction], new AlwaysHitCalculator(), randomSeed);
    var executor = new BattleActionExecutor(session);
    var attacker = executor.Submit(BattleAction.SpawnUnit(
        BattleTestFactory.MakeCombatant("Alpha", playerFaction), new Vector3I(4, 0, 1), attackerWeapon))
      .RequireSingleResult().AffectedUnit.RequireSome();
    var targetCombatant = BattleTestFactory.MakeCombatant("Hostile", enemyFaction, health: targetHealth);
    var targetWeapon = BattleTestFactory.MakeWeapon("Enemy Rifle");
    var target = executor.Submit(new SpawnUnit(targetCombatant, new Vector3I(4, 0, 4), Some(targetWeapon), Optional(targetArmor)))
      .RequireSingleResult().AffectedUnit.RequireSome();
    StartBattle(session);
    return new Fixture(session, executor, playerFaction, enemyFaction, attacker, target);
  }

  private static void EndTurn(Fixture fixture, FunProject.Combatants.Faction side)
  {
    var result = fixture.Executor.Submit(BattleAction.EndFactionTurn(side)).RequireSingleResult();
    Assert.True(result.Succeeded);
  }

  private static void Attack(Fixture fixture)
  {
    var result = fixture.Executor.Submit(BattleAction.AttackUnit(fixture.Attacker, fixture.Target)).RequireSingleResult();
    Assert.True(result.Succeeded);
  }

  [TestCase(TestName = "A hit applies the packet's status after the damage event")]
  public void HitAppliesPacketStatus()
  {
    var burn = new DamageOverTimeStatusSpecData { Name = "Burn", DurationTurns = 2, TickDamage = 2, TickElement = Element.Thermal };
    var fixture = MakeFixture(MakeStatusWeapon(burn));
    var raisedEvents = new List<BattleEvent>();
    fixture.Session.BattleEventCommitted += raisedEvents.Add;

    Attack(fixture);

    var active = fixture.Target.ActiveStatusEffects.Single();
    Assert.Equal(burn, active.Spec);
    Assert.Equal(2, active.RemainingTurns);
    var appliedEvent = raisedEvents.OfType<UnitStatusEffectAppliedBattleEvent>().Single();
    Assert.Equal(2, appliedEvent.RemainingTurns);
    int damagedIndex = raisedEvents.FindIndex(battleEvent => battleEvent is UnitDamagedBattleEvent);
    int appliedIndex = raisedEvents.FindIndex(battleEvent => battleEvent is UnitStatusEffectAppliedBattleEvent);
    Assert.True(damagedIndex >= 0);
    Assert.True(appliedIndex > damagedIndex);
  }

  [TestCase(TestName = "RequiresHealthDamage is blocked by absorbing armor and passes once damage spills")]
  public void RequiresHealthDamageGatesOnSpill()
  {
    var stun = new ImmobilizeStatusSpecData { Name = "Stun", DurationTurns = 1, RequiresHealthDamage = true };
    // 3 Kinetic vs 10 Thermal armor: fully absorbed at 1x, no spill.
    var fixture = MakeFixture(
      MakeStatusWeapon(stun, damage: 3, element: Element.Kinetic),
      BattleTestFactory.MakeArmor("Plating", armor: 10, element: Element.Thermal));

    Attack(fixture);
    Assert.False(fixture.Target.IsImmobilized);

    // Wear armor down to 1 (Kinetic ApplyDamage vs Thermal armor stays 1x): next hit spills 2.
    fixture.Executor.Submit(BattleAction.ApplyDamage(fixture.Target, 6)).RequireSingleResult();
    Attack(fixture);

    Assert.True(fixture.Target.IsImmobilized);
  }

  [TestCase(TestName = "Apply chance rolls on the session RNG")]
  public void ApplyChanceRollsOnSessionRng()
  {
    const int seed = 1234;
    const int chance = 35;
    var mirror = new Random(seed);
    mirror.Next(100);                              // consumed by the attack's to-hit roll
    bool shouldApply = mirror.Next(100) < chance;  // consumed by the status apply roll

    var burn = new DamageOverTimeStatusSpecData
    {
      Name = "Burn",
      DurationTurns = 2,
      TickDamage = 2,
      TickElement = Element.Thermal,
      ApplyChancePercent = chance,
    };
    var fixture = MakeFixture(MakeStatusWeapon(burn), randomSeed: seed);

    Attack(fixture);

    Assert.Equal(shouldApply, fixture.Target.ActiveStatusEffects.Any());
  }

  [TestCase(TestName = "Re-applying a status keeps a single instance")]
  public void ReapplyingKeepsSingleInstance()
  {
    var burn = new DamageOverTimeStatusSpecData { Name = "Burn", DurationTurns = 2, TickDamage = 2, TickElement = Element.Thermal };
    var fixture = MakeFixture(MakeStatusWeapon(burn));
    var raisedEvents = new List<BattleEvent>();
    fixture.Session.BattleEventCommitted += raisedEvents.Add;

    Attack(fixture);
    Attack(fixture);

    Assert.Equal(1, fixture.Target.ActiveStatusEffects.Count);
    Assert.Equal(2, raisedEvents.OfType<UnitStatusEffectAppliedBattleEvent>().Count());
  }

  [TestCase(TestName = "A killing blow does not apply statuses")]
  public void KillingBlowDoesNotApplyStatuses()
  {
    var burn = new DamageOverTimeStatusSpecData { Name = "Burn", DurationTurns = 2, TickDamage = 2, TickElement = Element.Thermal };
    var fixture = MakeFixture(MakeStatusWeapon(burn, damage: 5), targetHealth: 4);
    var raisedEvents = new List<BattleEvent>();
    fixture.Session.BattleEventCommitted += raisedEvents.Add;

    Attack(fixture);

    Assert.True(fixture.Target.IsDead);
    Assert.False(raisedEvents.OfType<UnitStatusEffectAppliedBattleEvent>().Any());
    Assert.Equal(0, fixture.Target.ActiveStatusEffects.Count);
  }

  [TestCase(TestName = "An immobilized unit cannot act")]
  public void ImmobilizedUnitCannotAct()
  {
    var fixture = MakeFixture(BattleTestFactory.MakeWeapon("Rifle"));
    fixture.Attacker.ApplyStatusEffect(new ImmobilizeStatusSpecData { Name = "Stun", DurationTurns = 1 });

    var result = fixture.Executor.Submit(BattleAction.AttackUnit(fixture.Attacker, fixture.Target)).RequireSingleResult();

    Assert.False(result.Succeeded);
    Assert.Equal(fixture.Target.MaxHealth, fixture.Target.CurrentHealth);
    Assert.Equal(fixture.Attacker.MaxActionPoints, fixture.Attacker.CurrentActionPoints);
    Assert.False(fixture.Session.CanUnitActNow(fixture.Attacker));

    var passResult = fixture.Executor.Submit(BattleAction.PassUnit(fixture.Attacker)).RequireSingleResult();
    Assert.False(passResult.Succeeded);
  }

  [TestCase(TestName = "Faction turn auto-ends when remaining units are immobilized")]
  public void FactionTurnAutoEndsWhenRemainingUnitsImmobilized()
  {
    var fixture = MakeFixture(BattleTestFactory.MakeWeapon("Rifle"));
    var second = SpawnUnit(fixture.Session, BattleTestFactory.MakeCombatant("Bravo", fixture.PlayerFaction), new Vector3I(1, 0, 1)).State;
    second.ApplyStatusEffect(new ImmobilizeStatusSpecData { Name = "Stun", DurationTurns = 1 });

    var result = fixture.Executor.Submit(BattleAction.PassUnit(fixture.Attacker)).RequireSingleResult();

    Assert.True(result.Succeeded);
    Assert.Equal(fixture.EnemyFaction, fixture.Session.ActiveSide);
  }

  [TestCase(TestName = "DoT ticks at the owner's turn end and expires after its duration")]
  public void DotTicksAtOwnersTurnEndAndExpires()
  {
    var burn = new DamageOverTimeStatusSpecData { Name = "Burn", DurationTurns = 2, TickDamage = 2, TickElement = Element.Thermal };
    var fixture = MakeFixture(MakeStatusWeapon(burn, damage: 3), targetHealth: 20);
    Attack(fixture);
    Assert.Equal(17, fixture.Target.CurrentHealth);

    EndTurn(fixture, fixture.PlayerFaction);          // attacker's turn end: no tick on the enemy unit
    Assert.Equal(17, fixture.Target.CurrentHealth);

    var raisedEvents = new List<BattleEvent>();
    fixture.Session.BattleEventCommitted += raisedEvents.Add;
    EndTurn(fixture, fixture.EnemyFaction);           // tick: 2 damage, 2 -> 1 remaining

    Assert.Equal(15, fixture.Target.CurrentHealth);
    var ticked = raisedEvents.OfType<UnitStatusEffectTickedBattleEvent>().Single();
    Assert.Equal(1, ticked.RemainingTurns);
    int turnEndedIndex = raisedEvents.FindIndex(battleEvent => battleEvent is TurnEndedBattleEvent);
    int tickedIndex = raisedEvents.FindIndex(battleEvent => battleEvent is UnitStatusEffectTickedBattleEvent);
    int damagedIndex = raisedEvents.FindIndex(battleEvent => battleEvent is UnitDamagedBattleEvent);
    Assert.True(tickedIndex > turnEndedIndex);
    Assert.True(damagedIndex > tickedIndex);

    EndTurn(fixture, fixture.PlayerFaction);
    raisedEvents.Clear();
    EndTurn(fixture, fixture.EnemyFaction);           // tick: 2 damage, 1 -> 0, expires

    Assert.Equal(13, fixture.Target.CurrentHealth);
    Assert.True(raisedEvents.OfType<UnitStatusEffectExpiredBattleEvent>().Any());
    Assert.Equal(0, fixture.Target.ActiveStatusEffects.Count);

    EndTurn(fixture, fixture.PlayerFaction);
    raisedEvents.Clear();
    EndTurn(fixture, fixture.EnemyFaction);           // nothing left to tick

    Assert.Equal(13, fixture.Target.CurrentHealth);
    Assert.False(raisedEvents.OfType<UnitStatusEffectTickedBattleEvent>().Any());
  }

  [TestCase(TestName = "DoT ticks resolve through armor with elemental matching")]
  public void DotTicksResolveThroughArmor()
  {
    var burn = new DamageOverTimeStatusSpecData { Name = "Burn", DurationTurns = 1, TickDamage = 2, TickElement = Element.Thermal };
    var fixture = MakeFixture(
      MakeStatusWeapon(burn, damage: 3, element: Element.Kinetic),
      BattleTestFactory.MakeArmor("Thermal Plating", armor: 10, element: Element.Thermal));
    Attack(fixture);                                  // 3 Kinetic absorbed at 1x: armor 10 -> 7
    var armor = fixture.Target.EquippedArmor.RequireSome().Capability;
    Assert.Equal(7, armor.Current);
    Assert.Equal(fixture.Target.MaxHealth, fixture.Target.CurrentHealth);

    EndTurn(fixture, fixture.PlayerFaction);
    EndTurn(fixture, fixture.EnemyFaction);           // tick 2 Thermal vs Thermal armor: 1.5x -> 3 armor

    Assert.Equal(4, armor.Current);
    Assert.Equal(fixture.Target.MaxHealth, fixture.Target.CurrentHealth);
  }

  [TestCase(TestName = "An active DoT suppresses armor regen by re-arming its delay")]
  public void DotSuppressesArmorRegen()
  {
    var burn = new DamageOverTimeStatusSpecData { Name = "Burn", DurationTurns = 2, TickDamage = 2, TickElement = Element.Thermal };
    var fixture = MakeFixture(
      MakeStatusWeapon(burn, damage: 3, element: Element.Thermal),
      BattleTestFactory.MakeArmor("Recharger", armor: 10, element: Element.Kinetic, regenDelayTurns: 1, regenPerTurn: 3));
    Attack(fixture);                                  // 3 Thermal vs Kinetic armor at 1x: armor 10 -> 7, delay armed
    var armor = fixture.Target.EquippedArmor.RequireSome().Capability;
    Assert.Equal(7, armor.Current);

    var raisedEvents = new List<BattleEvent>();
    fixture.Session.BattleEventCommitted += raisedEvents.Add;

    EndTurn(fixture, fixture.PlayerFaction);
    EndTurn(fixture, fixture.EnemyFaction);           // tick: armor 7 -> 5 and delay re-armed before regen runs
    Assert.Equal(5, armor.Current);

    EndTurn(fixture, fixture.PlayerFaction);
    EndTurn(fixture, fixture.EnemyFaction);           // tick: armor 5 -> 3, burn expires; regen still suppressed
    Assert.Equal(3, armor.Current);
    Assert.False(raisedEvents.OfType<UnitArmorRegeneratedBattleEvent>().Any());

    EndTurn(fixture, fixture.PlayerFaction);
    EndTurn(fixture, fixture.EnemyFaction);           // burn gone: delay already counted down, regen restores 3
    Assert.Equal(6, armor.Current);
    Assert.True(raisedEvents.OfType<UnitArmorRegeneratedBattleEvent>().Any());
  }

  [TestCase(TestName = "A lethal tick kills the unit and the turn advances cleanly")]
  public void LethalTickKillsAndTurnAdvances()
  {
    var burn = new DamageOverTimeStatusSpecData { Name = "Burn", DurationTurns = 3, TickDamage = 2, TickElement = Element.Thermal };
    var fixture = MakeFixture(MakeStatusWeapon(burn, damage: 3), targetHealth: 4);
    Attack(fixture);                                  // 4 -> 1 health, burning
    Assert.True(fixture.Target.IsAlive);

    EndTurn(fixture, fixture.PlayerFaction);
    var raisedEvents = new List<BattleEvent>();
    fixture.Session.BattleEventCommitted += raisedEvents.Add;
    EndTurn(fixture, fixture.EnemyFaction);           // tick 2 kills at the enemy's own turn end

    Assert.True(fixture.Target.IsDead);
    Assert.True(raisedEvents.OfType<UnitKilledBattleEvent>().Any());
    Assert.Equal(BattlePhase.InProgress, fixture.Session.Phase);
    Assert.Equal(fixture.PlayerFaction, fixture.Session.ActiveSide);
  }

  [TestCase(TestName = "A stunned unit loses its turn and recovers after its own turn end")]
  public void StunnedUnitLosesTurnAndRecovers()
  {
    var stun = new ImmobilizeStatusSpecData { Name = "Stun", DurationTurns = 1 };
    var fixture = MakeFixture(MakeStatusWeapon(stun));
    Attack(fixture);
    Assert.True(fixture.Target.IsImmobilized);

    EndTurn(fixture, fixture.PlayerFaction);          // enemy turn: stunned target cannot act
    Assert.False(fixture.Session.CanUnitActNow(fixture.Target));
    var attackBack = fixture.Executor.Submit(BattleAction.AttackUnit(fixture.Target, fixture.Attacker)).RequireSingleResult();
    Assert.False(attackBack.Succeeded);

    var raisedEvents = new List<BattleEvent>();
    fixture.Session.BattleEventCommitted += raisedEvents.Add;
    EndTurn(fixture, fixture.EnemyFaction);           // stun ticks 1 -> 0 and expires

    Assert.False(fixture.Target.IsImmobilized);
    Assert.True(raisedEvents.OfType<UnitStatusEffectExpiredBattleEvent>().Any());
  }

  [TestCase(TestName = "A lethal tick stops the unit's remaining statuses")]
  public void LethalTickStopsRemainingStatuses()
  {
    var fixture = MakeFixture(BattleTestFactory.MakeWeapon("Rifle"), targetHealth: 2);
    // Both lethal, so the assertion holds regardless of status iteration order.
    var burn = new DamageOverTimeStatusSpecData { Name = "Burn", DurationTurns = 2, TickDamage = 5, TickElement = Element.Thermal };
    var acid = new DamageOverTimeStatusSpecData { Name = "Acid", DurationTurns = 2, TickDamage = 5, TickElement = Element.Chem };
    fixture.Target.ApplyStatusEffect(burn);
    fixture.Target.ApplyStatusEffect(acid);

    EndTurn(fixture, fixture.PlayerFaction);
    var raisedEvents = new List<BattleEvent>();
    fixture.Session.BattleEventCommitted += raisedEvents.Add;
    EndTurn(fixture, fixture.EnemyFaction);           // first tick kills; the second status never ticks

    Assert.True(fixture.Target.IsDead);
    Assert.Equal(1, raisedEvents.OfType<UnitStatusEffectTickedBattleEvent>().Count());
    Assert.True(raisedEvents.OfType<UnitKilledBattleEvent>().Any());
    Assert.False(raisedEvents.OfType<UnitStatusEffectExpiredBattleEvent>().Any());
    Assert.Equal(2, fixture.Target.ActiveStatusEffects.Count);
    Assert.Equal(1, fixture.Target.ActiveStatusEffects.Count(effect => effect.RemainingTurns == 1));
    Assert.Equal(1, fixture.Target.ActiveStatusEffects.Count(effect => effect.RemainingTurns == 2));
    Assert.Equal(BattlePhase.InProgress, fixture.Session.Phase);
    Assert.Equal(fixture.PlayerFaction, fixture.Session.ActiveSide);
  }

  [TestCase(TestName = "Multiple statuses on one unit each tick at the owner's turn end")]
  public void MultipleStatusesEachTick()
  {
    var fixture = MakeFixture(BattleTestFactory.MakeWeapon("Rifle"), targetHealth: 20);
    var burn = new DamageOverTimeStatusSpecData { Name = "Burn", DurationTurns = 2, TickDamage = 2, TickElement = Element.Thermal };
    var poison = new DamageOverTimeStatusSpecData { Name = "Poison", DurationTurns = 3, TickDamage = 1, TickElement = Element.Chem };
    fixture.Target.ApplyStatusEffect(burn);
    fixture.Target.ApplyStatusEffect(poison);

    EndTurn(fixture, fixture.PlayerFaction);
    var raisedEvents = new List<BattleEvent>();
    fixture.Session.BattleEventCommitted += raisedEvents.Add;
    EndTurn(fixture, fixture.EnemyFaction);           // both tick: 2 + 1 damage

    Assert.Equal(17, fixture.Target.CurrentHealth);
    Assert.Equal(2, raisedEvents.OfType<UnitStatusEffectTickedBattleEvent>().Count());
    Assert.Equal(2, fixture.Target.ActiveStatusEffects.Count);
    Assert.Equal(1, fixture.Target.ActiveStatusEffects.Count(effect => effect.RemainingTurns == 1));
    Assert.Equal(1, fixture.Target.ActiveStatusEffects.Count(effect => effect.RemainingTurns == 2));
  }
}
