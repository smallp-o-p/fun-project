using FunProject.Battle;
using FunProject.Core;
using GdUnit4;
using Godot;
using System;

[TestSuite]
[RequireGodotRuntime]
public partial class StatusEffectBattleTest
{
  [TestCase(TestName = "A hit applies the packet's status after the damage event")]
  public void HitAppliesPacketStatus()
  {
    var burn = BattleTestFactory.MakeBurn();
    var battle = new BattleDuelBuilder
    {
      HitChanceCalculator = new AlwaysHitCalculator(),
      Player = new DuelSide("Alpha", Weapon: BattleTestFactory.MakeStatusWeapon(burn)),
      Enemy = new DuelSide("Hostile", Weapon: BattleTestFactory.MakeWeapon("Enemy Rifle")),
    }.Start();
    var target = battle.EnemyUnit.State;
    var recorder = new BattleEventRecorder(battle.Session);

    Attack(battle.Session, battle.Executor, battle.PlayerUnit.State, battle.EnemyUnit.State);

    var active = target.ActiveStatusEffects.AsValueEnumerable().Single();
    Assert.Equal(burn, active.Spec);
    Assert.Equal(2, active.RemainingTurns);
    var appliedEvent = recorder.Single<UnitStatusEffectAppliedBattleEvent>();
    Assert.Equal(2, appliedEvent.RemainingTurns);
    recorder.AssertCommittedBefore<UnitDamagedBattleEvent, UnitStatusEffectAppliedBattleEvent>();
  }

  [TestCase(TestName = "RequiresHealthDamage is blocked by absorbing armor and passes once damage spills")]
  public void RequiresHealthDamageGatesOnSpill()
  {
    var stun = BattleTestFactory.MakeStun(requiresHealthDamage: true);
    // 3 Kinetic vs 10 Thermal armor: fully absorbed at 1x, no spill.
    var battle = new BattleDuelBuilder
    {
      HitChanceCalculator = new AlwaysHitCalculator(),
      Player = new DuelSide("Alpha", Weapon: BattleTestFactory.MakeStatusWeapon(stun, damage: 3, element: Element.Kinetic)),
      Enemy = new DuelSide(
        "Hostile",
        Weapon: BattleTestFactory.MakeWeapon("Enemy Rifle"),
        Armor: BattleTestFactory.MakeArmor("Plating", armor: 10, element: Element.Thermal)),
    }.Start();
    var target = battle.EnemyUnit.State;

    Attack(battle.Session, battle.Executor, battle.PlayerUnit.State, battle.EnemyUnit.State);
    Assert.False(target.IsImmobilized);

    // Wear armor down to 1 (Kinetic ApplyDamage vs Thermal armor stays 1x): next hit spills 2.
    ApplyDamage(battle.Session, target, 6);
    Attack(battle.Session, battle.Executor, battle.PlayerUnit.State, battle.EnemyUnit.State);

    Assert.True(target.IsImmobilized);
  }

  [TestCase(TestName = "Apply chance rolls on the session RNG")]
  public void ApplyChanceRollsOnSessionRng()
  {
    const int seed = 1234;
    const int chance = 35;
    var mirror = new Random(seed);
    mirror.Next(100);                              // consumed by the attack's to-hit roll
    bool shouldApply = mirror.Next(100) < chance;  // consumed by the status apply roll

    var burn = BattleTestFactory.MakeBurn(applyChancePercent: chance);
    var battle = new BattleDuelBuilder
    {
      HitChanceCalculator = new AlwaysHitCalculator(),
      RandomSeed = seed,
      Player = new DuelSide("Alpha", Weapon: BattleTestFactory.MakeStatusWeapon(burn)),
      Enemy = new DuelSide("Hostile", Weapon: BattleTestFactory.MakeWeapon("Enemy Rifle")),
    }.Start();
    var target = battle.EnemyUnit.State;

    Attack(battle.Session, battle.Executor, battle.PlayerUnit.State, battle.EnemyUnit.State);

    Assert.Equal(shouldApply, target.ActiveStatusEffects.AsValueEnumerable().Any());
  }

  [TestCase(TestName = "Re-applying a status keeps a single instance")]
  public void ReapplyingKeepsSingleInstance()
  {
    var burn = BattleTestFactory.MakeBurn();
    var battle = new BattleDuelBuilder
    {
      HitChanceCalculator = new AlwaysHitCalculator(),
      Player = new DuelSide("Alpha", Weapon: BattleTestFactory.MakeStatusWeapon(burn)),
      Enemy = new DuelSide("Hostile", Weapon: BattleTestFactory.MakeWeapon("Enemy Rifle")),
    }.Start();
    var target = battle.EnemyUnit.State;
    var recorder = new BattleEventRecorder(battle.Session);

    Attack(battle.Session, battle.Executor, battle.PlayerUnit.State, battle.EnemyUnit.State);
    Attack(battle.Session, battle.Executor, battle.PlayerUnit.State, battle.EnemyUnit.State);

    Assert.Equal(1, target.ActiveStatusEffects.Count);
    Assert.Equal(2, recorder.OfType<UnitStatusEffectAppliedBattleEvent>().AsValueEnumerable().Count());
  }

  [TestCase(TestName = "A killing blow does not apply statuses")]
  public void KillingBlowDoesNotApplyStatuses()
  {
    var burn = BattleTestFactory.MakeBurn();
    var battle = new BattleDuelBuilder
    {
      HitChanceCalculator = new AlwaysHitCalculator(),
      Player = new DuelSide("Alpha", Weapon: BattleTestFactory.MakeStatusWeapon(burn, damage: 5)),
      Enemy = new DuelSide("Hostile", Health: 4, Weapon: BattleTestFactory.MakeWeapon("Enemy Rifle")),
    }.Start();
    var target = battle.EnemyUnit.State;
    var recorder = new BattleEventRecorder(battle.Session);

    Attack(battle.Session, battle.Executor, battle.PlayerUnit.State, battle.EnemyUnit.State);

    Assert.True(target.IsDead);
    Assert.False(recorder.OfType<UnitStatusEffectAppliedBattleEvent>().AsValueEnumerable().Any());
    Assert.Equal(0, target.ActiveStatusEffects.Count);
  }

  [TestCase(TestName = "Faction turn auto-ends when remaining units are immobilized")]
  public void FactionTurnAutoEndsWhenRemainingUnitsImmobilized()
  {
    var battle = new BattleDuelBuilder
    {
      HitChanceCalculator = new AlwaysHitCalculator(),
      Player = new DuelSide("Alpha", Weapon: BattleTestFactory.MakeWeapon("Rifle")),
      Enemy = new DuelSide("Hostile", Weapon: BattleTestFactory.MakeWeapon("Enemy Rifle")),
    }.Start();
    var attacker = battle.PlayerUnit.State;
    var second = SpawnUnit(battle.Session, BattleTestFactory.MakeCombatant("Bravo", battle.PlayerFaction), new Vector3I(1, 0, 1)).State;
    second.ApplyStatusEffect(BattleTestFactory.MakeStun());

    battle.Executor.Submit(BattleAction.PassUnit(battle.PlayerUnit.AliveIn(battle.Session)));

    Assert.Equal(battle.EnemyFaction, battle.Session.ActiveSide);
  }

  [TestCase(TestName = "DoT ticks at the owner's turn end and expires after its duration")]
  public void DotTicksAtOwnersTurnEndAndExpires()
  {
    var burn = BattleTestFactory.MakeBurn();
    var battle = new BattleDuelBuilder
    {
      HitChanceCalculator = new AlwaysHitCalculator(),
      Player = new DuelSide("Alpha", Weapon: BattleTestFactory.MakeStatusWeapon(burn, damage: 3)),
      Enemy = new DuelSide("Hostile", Health: 20, Weapon: BattleTestFactory.MakeWeapon("Enemy Rifle")),
    }.Start();
    var target = battle.EnemyUnit.State;
    Attack(battle.Session, battle.Executor, battle.PlayerUnit.State, battle.EnemyUnit.State);
    Assert.Equal(17, target.CurrentHealth);

    EndFactionTurn(battle.Executor, battle.PlayerFaction);   // attacker's turn end: no tick on the enemy unit
    Assert.Equal(17, target.CurrentHealth);

    var recorder = new BattleEventRecorder(battle.Session);
    EndFactionTurn(battle.Executor, battle.EnemyFaction);    // tick: 2 damage, 2 -> 1 remaining

    Assert.Equal(15, target.CurrentHealth);
    var ticked = recorder.Single<UnitStatusEffectTickedBattleEvent>();
    Assert.Equal(1, ticked.RemainingTurns);
    recorder.AssertCommittedBefore<TurnEndedBattleEvent, UnitStatusEffectTickedBattleEvent>();
    recorder.AssertCommittedBefore<UnitStatusEffectTickedBattleEvent, UnitDamagedBattleEvent>();

    EndFactionTurn(battle.Executor, battle.PlayerFaction);
    recorder = new BattleEventRecorder(battle.Session);
    EndFactionTurn(battle.Executor, battle.EnemyFaction);    // tick: 2 damage, 1 -> 0, expires

    Assert.Equal(13, target.CurrentHealth);
    Assert.True(recorder.OfType<UnitStatusEffectExpiredBattleEvent>().AsValueEnumerable().Any());
    Assert.Equal(0, target.ActiveStatusEffects.Count);

    EndFactionTurn(battle.Executor, battle.PlayerFaction);
    recorder = new BattleEventRecorder(battle.Session);
    EndFactionTurn(battle.Executor, battle.EnemyFaction);    // nothing left to tick

    Assert.Equal(13, target.CurrentHealth);
    Assert.False(recorder.OfType<UnitStatusEffectTickedBattleEvent>().AsValueEnumerable().Any());
  }

  [TestCase(TestName = "DoT ticks resolve through armor with elemental matching")]
  public void DotTicksResolveThroughArmor()
  {
    var burn = BattleTestFactory.MakeBurn(duration: 1);
    var battle = new BattleDuelBuilder
    {
      HitChanceCalculator = new AlwaysHitCalculator(),
      Player = new DuelSide("Alpha", Weapon: BattleTestFactory.MakeStatusWeapon(burn, damage: 3, element: Element.Kinetic)),
      Enemy = new DuelSide(
        "Hostile",
        Weapon: BattleTestFactory.MakeWeapon("Enemy Rifle"),
        Armor: BattleTestFactory.MakeArmor("Thermal Plating", armor: 10, element: Element.Thermal)),
    }.Start();
    var target = battle.EnemyUnit.State;
    Attack(battle.Session, battle.Executor, battle.PlayerUnit.State, battle.EnemyUnit.State);   // 3 Kinetic absorbed at 1x: armor 10 -> 7
    var armor = target.EquippedArmor.RequireSome().Capability;
    Assert.Equal(7, armor.Current);
    Assert.Equal(target.MaxHealth, target.CurrentHealth);

    EndFactionTurn(battle.Executor, battle.PlayerFaction);
    EndFactionTurn(battle.Executor, battle.EnemyFaction);           // tick 2 Thermal vs Thermal armor: 1.5x -> 3 armor

    Assert.Equal(4, armor.Current);
    Assert.Equal(target.MaxHealth, target.CurrentHealth);
  }

  [TestCase(TestName = "An active DoT suppresses armor regen by re-arming its delay")]
  public void DotSuppressesArmorRegen()
  {
    var burn = BattleTestFactory.MakeBurn();
    var battle = new BattleDuelBuilder
    {
      HitChanceCalculator = new AlwaysHitCalculator(),
      Player = new DuelSide("Alpha", Weapon: BattleTestFactory.MakeStatusWeapon(burn, damage: 3, element: Element.Thermal)),
      Enemy = new DuelSide(
        "Hostile",
        Weapon: BattleTestFactory.MakeWeapon("Enemy Rifle"),
        Armor: BattleTestFactory.MakeArmor("Recharger", armor: 10, element: Element.Kinetic, regenDelayTurns: 1, regenPerTurn: 3)),
    }.Start();
    var target = battle.EnemyUnit.State;
    Attack(battle.Session, battle.Executor, battle.PlayerUnit.State, battle.EnemyUnit.State);   // 3 Thermal vs Kinetic armor at 1x: armor 10 -> 7, delay armed
    var armor = target.EquippedArmor.RequireSome().Capability;
    Assert.Equal(7, armor.Current);

    var recorder = new BattleEventRecorder(battle.Session);

    EndFactionTurn(battle.Executor, battle.PlayerFaction);
    EndFactionTurn(battle.Executor, battle.EnemyFaction);           // tick: armor 7 -> 5 and delay re-armed before regen runs
    Assert.Equal(5, armor.Current);

    EndFactionTurn(battle.Executor, battle.PlayerFaction);
    EndFactionTurn(battle.Executor, battle.EnemyFaction);           // tick: armor 5 -> 3, burn expires; regen still suppressed
    Assert.Equal(3, armor.Current);
    Assert.False(recorder.OfType<UnitArmorRegeneratedBattleEvent>().AsValueEnumerable().Any());

    EndFactionTurn(battle.Executor, battle.PlayerFaction);
    EndFactionTurn(battle.Executor, battle.EnemyFaction);           // burn gone: delay already counted down, regen restores 3
    Assert.Equal(6, armor.Current);
    Assert.True(recorder.OfType<UnitArmorRegeneratedBattleEvent>().AsValueEnumerable().Any());
  }

  [TestCase(TestName = "A lethal tick kills the unit and the turn advances cleanly")]
  public void LethalTickKillsAndTurnAdvances()
  {
    var burn = BattleTestFactory.MakeBurn(duration: 3);
    var battle = new BattleDuelBuilder
    {
      HitChanceCalculator = new AlwaysHitCalculator(),
      Player = new DuelSide("Alpha", Weapon: BattleTestFactory.MakeStatusWeapon(burn, damage: 3)),
      Enemy = new DuelSide("Hostile", Health: 4, Weapon: BattleTestFactory.MakeWeapon("Enemy Rifle")),
    }.Start();
    var target = battle.EnemyUnit.State;
    Attack(battle.Session, battle.Executor, battle.PlayerUnit.State, battle.EnemyUnit.State);   // 4 -> 1 health, burning
    Assert.True(target.IsAlive);

    EndFactionTurn(battle.Executor, battle.PlayerFaction);
    var recorder = new BattleEventRecorder(battle.Session);
    EndFactionTurn(battle.Executor, battle.EnemyFaction);           // tick 2 kills at the enemy's own turn end

    Assert.True(target.IsDead);
    Assert.True(recorder.OfType<UnitKilledBattleEvent>().AsValueEnumerable().Any());
    Assert.Equal(BattlePhase.InProgress, battle.Session.Phase);
    Assert.Equal(battle.PlayerFaction, battle.Session.ActiveSide);
  }

  [TestCase(TestName = "A stunned unit loses its turn and recovers after its own turn end")]
  public void StunnedUnitLosesTurnAndRecovers()
  {
    var stun = BattleTestFactory.MakeStun();
    var battle = new BattleDuelBuilder
    {
      HitChanceCalculator = new AlwaysHitCalculator(),
      Player = new DuelSide("Alpha", Weapon: BattleTestFactory.MakeStatusWeapon(stun)),
      Enemy = new DuelSide("Hostile", Weapon: BattleTestFactory.MakeWeapon("Enemy Rifle")),
    }.Start();
    var target = battle.EnemyUnit.State;
    Attack(battle.Session, battle.Executor, battle.PlayerUnit.State, battle.EnemyUnit.State);
    Assert.True(target.IsImmobilized);

    EndFactionTurn(battle.Executor, battle.PlayerFaction);          // enemy turn: stunned target cannot act
    Assert.False(battle.Session.CanUnitActNow(target));

    var recorder = new BattleEventRecorder(battle.Session);
    EndFactionTurn(battle.Executor, battle.EnemyFaction);           // stun ticks 1 -> 0 and expires

    Assert.False(target.IsImmobilized);
    Assert.True(recorder.OfType<UnitStatusEffectExpiredBattleEvent>().AsValueEnumerable().Any());
  }

  [TestCase(TestName = "A lethal tick stops the unit's remaining statuses")]
  public void LethalTickStopsRemainingStatuses()
  {
    var battle = new BattleDuelBuilder
    {
      HitChanceCalculator = new AlwaysHitCalculator(),
      Player = new DuelSide("Alpha", Weapon: BattleTestFactory.MakeWeapon("Rifle")),
      Enemy = new DuelSide("Hostile", Health: 2, Weapon: BattleTestFactory.MakeWeapon("Enemy Rifle")),
    }.Start();
    var target = battle.EnemyUnit.State;
    // Both lethal, so the assertion holds regardless of status iteration order.
    var burn = BattleTestFactory.MakeBurn(tickDamage: 5);
    var acid = BattleTestFactory.MakeBurn(tickDamage: 5, tickElement: Element.Chem, name: "Acid");
    target.ApplyStatusEffect(burn);
    target.ApplyStatusEffect(acid);

    EndFactionTurn(battle.Executor, battle.PlayerFaction);
    var recorder = new BattleEventRecorder(battle.Session);
    EndFactionTurn(battle.Executor, battle.EnemyFaction);           // first tick kills; the second status never ticks

    Assert.True(target.IsDead);
    Assert.Equal(1, recorder.OfType<UnitStatusEffectTickedBattleEvent>().AsValueEnumerable().Count());
    Assert.True(recorder.OfType<UnitKilledBattleEvent>().AsValueEnumerable().Any());
    Assert.False(recorder.OfType<UnitStatusEffectExpiredBattleEvent>().AsValueEnumerable().Any());
    Assert.Equal(2, target.ActiveStatusEffects.Count);
    Assert.Equal(1, target.ActiveStatusEffects.AsValueEnumerable().Count(effect => effect.RemainingTurns == 1));
    Assert.Equal(1, target.ActiveStatusEffects.AsValueEnumerable().Count(effect => effect.RemainingTurns == 2));
    Assert.Equal(BattlePhase.InProgress, battle.Session.Phase);
    Assert.Equal(battle.PlayerFaction, battle.Session.ActiveSide);
  }

  [TestCase(TestName = "Multiple statuses on one unit each tick at the owner's turn end")]
  public void MultipleStatusesEachTick()
  {
    var battle = new BattleDuelBuilder
    {
      HitChanceCalculator = new AlwaysHitCalculator(),
      Player = new DuelSide("Alpha", Weapon: BattleTestFactory.MakeWeapon("Rifle")),
      Enemy = new DuelSide("Hostile", Health: 20, Weapon: BattleTestFactory.MakeWeapon("Enemy Rifle")),
    }.Start();
    var target = battle.EnemyUnit.State;
    var burn = BattleTestFactory.MakeBurn();
    var poison = BattleTestFactory.MakeBurn(duration: 3, tickDamage: 1, tickElement: Element.Chem, name: "Poison");
    target.ApplyStatusEffect(burn);
    target.ApplyStatusEffect(poison);

    EndFactionTurn(battle.Executor, battle.PlayerFaction);
    var recorder = new BattleEventRecorder(battle.Session);
    EndFactionTurn(battle.Executor, battle.EnemyFaction);           // both tick: 2 + 1 damage

    Assert.Equal(17, target.CurrentHealth);
    Assert.Equal(2, recorder.OfType<UnitStatusEffectTickedBattleEvent>().AsValueEnumerable().Count());
    Assert.Equal(2, target.ActiveStatusEffects.Count);
    Assert.Equal(1, target.ActiveStatusEffects.AsValueEnumerable().Count(effect => effect.RemainingTurns == 1));
    Assert.Equal(1, target.ActiveStatusEffects.AsValueEnumerable().Count(effect => effect.RemainingTurns == 2));
  }
}
