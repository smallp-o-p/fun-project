using FunProject.Battle;
using FunProject.Core;
using GdUnit4;
using Godot;

[TestSuite]
[RequireGodotRuntime]
public partial class StatusEffectBattleTest
{
  [TestCase(TestName = "A hit applies the packet's status after the damage event")]
  public void HitAppliesPacketStatus()
  {
    var burn = TestData.MakeBurn();
    using var battle = BattleFixture.Duel(
      hitChanceCalculator: new AlwaysHitCalculator(),
      player: new("Alpha", Weapon: TestData.MakeStatusWeapon(burn)),
      enemy: new("Hostile", Weapon: TestData.MakeWeapon("Enemy Rifle")));
    var target = battle.EnemyUnit;
    battle.ClearEvents();

    battle.Attack(battle.PlayerUnit, battle.EnemyUnit);

    var active = target.ActiveStatusEffects.AsValueEnumerable().Single();
    Assert.Equal(burn, active.Spec);
    Assert.Equal(2, active.RemainingTurns);
    var appliedEvent = battle.Events.SingleEvent<UnitStatusEffectAppliedBattleEvent>();
    Assert.Equal(2, appliedEvent.RemainingTurns);
    battle.Events.EventBefore<UnitDamagedBattleEvent, UnitStatusEffectAppliedBattleEvent>();
  }

  [TestCase(TestName = "RequiresHealthDamage is blocked by absorbing armor and passes once damage spills")]
  public void RequiresHealthDamageGatesOnSpill()
  {
    var stun = TestData.MakeStun(requiresHealthDamage: true);
    // 3 Kinetic vs 10 Thermal armor: fully absorbed at 1x, no spill.
    using var battle = BattleFixture.Duel(
      hitChanceCalculator: new AlwaysHitCalculator(),
      player: new("Alpha", Weapon: TestData.MakeStatusWeapon(stun, damage: 3, element: Element.Kinetic)),
      enemy: new(
        "Hostile",
        Weapon: TestData.MakeWeapon("Enemy Rifle"),
        Armor: TestData.MakeArmor("Plating", armor: 10, element: Element.Thermal)));
    var target = battle.EnemyUnit;

    battle.Attack(battle.PlayerUnit, battle.EnemyUnit);
    Assert.False(target.IsImmobilized);

    // Wear armor down to 1 (Kinetic ApplyDamage vs Thermal armor stays 1x): next hit spills 2.
    battle.ApplyDamage(target, 6);
    battle.Attack(battle.PlayerUnit, battle.EnemyUnit);

    Assert.True(target.IsImmobilized);
  }

  [TestCase(TestName = "Apply chance rolls on the session RNG")]
  public void ApplyChanceRollsOnSessionRng()
  {
    const int seed = 1234;
    const int chance = 35;
    var mirror = new System.Random(seed);
    mirror.Next(100);                              // consumed by the attack's to-hit roll
    bool shouldApply = mirror.Next(100) < chance;  // consumed by the status apply roll

    var burn = TestData.MakeBurn(applyChancePercent: chance);
    using var battle = BattleFixture.Duel(
      hitChanceCalculator: new AlwaysHitCalculator(),
      randomSeed: seed,
      player: new("Alpha", Weapon: TestData.MakeStatusWeapon(burn)),
      enemy: new("Hostile", Weapon: TestData.MakeWeapon("Enemy Rifle")));
    var target = battle.EnemyUnit;

    battle.Attack(battle.PlayerUnit, battle.EnemyUnit);

    Assert.Equal(shouldApply, target.ActiveStatusEffects.AsValueEnumerable().Any());
  }

  [TestCase(TestName = "Re-applying a status keeps a single instance")]
  public void ReapplyingKeepsSingleInstance()
  {
    var burn = TestData.MakeBurn();
    using var battle = BattleFixture.Duel(
      hitChanceCalculator: new AlwaysHitCalculator(),
      player: new("Alpha", Weapon: TestData.MakeStatusWeapon(burn)),
      enemy: new("Hostile", Weapon: TestData.MakeWeapon("Enemy Rifle")));
    var target = battle.EnemyUnit;
    battle.ClearEvents();

    battle.Attack(battle.PlayerUnit, battle.EnemyUnit);
    battle.Attack(battle.PlayerUnit, battle.EnemyUnit);

    Assert.Equal(1, target.ActiveStatusEffects.Count);
    Assert.Equal(2, battle.Events.EventsOf<UnitStatusEffectAppliedBattleEvent>().AsValueEnumerable().Count());
  }

  [TestCase(TestName = "A killing blow does not apply statuses")]
  public void KillingBlowDoesNotApplyStatuses()
  {
    var burn = TestData.MakeBurn();
    using var battle = BattleFixture.Duel(
      hitChanceCalculator: new AlwaysHitCalculator(),
      player: new("Alpha", Weapon: TestData.MakeStatusWeapon(burn, damage: 5)),
      enemy: new("Hostile", Health: 4, Weapon: TestData.MakeWeapon("Enemy Rifle")));
    var target = battle.EnemyUnit;
    battle.ClearEvents();

    battle.Attack(battle.PlayerUnit, battle.EnemyUnit);

    Assert.True(target.IsDead);
    Assert.False(battle.Events.EventsOf<UnitStatusEffectAppliedBattleEvent>().AsValueEnumerable().Any());
    Assert.Equal(0, target.ActiveStatusEffects.Count);
  }

  [TestCase(TestName = "Faction turn auto-ends when remaining units are immobilized")]
  public void FactionTurnAutoEndsWhenRemainingUnitsImmobilized()
  {
    using var battle = BattleFixture.Duel(
      hitChanceCalculator: new AlwaysHitCalculator(),
      player: new("Alpha", Weapon: TestData.MakeWeapon("Rifle")),
      enemy: new("Hostile", Weapon: TestData.MakeWeapon("Enemy Rifle")));
    var attacker = battle.PlayerUnit;
    var second = battle.Spawn(TestData.MakeCombatant("Bravo", battle.PlayerFaction), new Vector3I(1, 0, 1));
    second.ApplyStatusEffect(TestData.MakeStun());

    battle.Pass(attacker);

    Assert.Equal(battle.EnemyFaction, battle.Session.ActiveSide);
  }

  [TestCase(TestName = "DoT ticks at the owner's turn end and expires after its duration")]
  public void DotTicksAtOwnersTurnEndAndExpires()
  {
    var burn = TestData.MakeBurn();
    using var battle = BattleFixture.Duel(
      hitChanceCalculator: new AlwaysHitCalculator(),
      player: new("Alpha", Weapon: TestData.MakeStatusWeapon(burn, damage: 3)),
      enemy: new("Hostile", Health: 20, Weapon: TestData.MakeWeapon("Enemy Rifle")));
    var target = battle.EnemyUnit;
    battle.Attack(battle.PlayerUnit, battle.EnemyUnit);
    Assert.Equal(17, target.CurrentHealth);

    battle.EndFactionTurn(battle.PlayerFaction);   // attacker's turn end: no tick on the enemy unit
    Assert.Equal(17, target.CurrentHealth);

    battle.ClearEvents();
    battle.EndFactionTurn(battle.EnemyFaction);    // tick: 2 damage, 2 -> 1 remaining

    Assert.Equal(15, target.CurrentHealth);
    var ticked = battle.Events.SingleEvent<UnitStatusEffectTickedBattleEvent>();
    Assert.Equal(1, ticked.RemainingTurns);
    battle.Events.EventBefore<TurnEndedBattleEvent, UnitStatusEffectTickedBattleEvent>();
    battle.Events.EventBefore<UnitStatusEffectTickedBattleEvent, UnitDamagedBattleEvent>();

    battle.EndFactionTurn(battle.PlayerFaction);
    battle.ClearEvents();
    battle.EndFactionTurn(battle.EnemyFaction);    // tick: 2 damage, 1 -> 0, expires

    Assert.Equal(13, target.CurrentHealth);
    Assert.True(battle.Events.EventsOf<UnitStatusEffectExpiredBattleEvent>().AsValueEnumerable().Any());
    Assert.Equal(0, target.ActiveStatusEffects.Count);

    battle.EndFactionTurn(battle.PlayerFaction);
    battle.ClearEvents();
    battle.EndFactionTurn(battle.EnemyFaction);    // nothing left to tick

    Assert.Equal(13, target.CurrentHealth);
    Assert.False(battle.Events.EventsOf<UnitStatusEffectTickedBattleEvent>().AsValueEnumerable().Any());
  }

  [TestCase(TestName = "DoT ticks resolve through armor with elemental matching")]
  public void DotTicksResolveThroughArmor()
  {
    var burn = TestData.MakeBurn(duration: 1);
    using var battle = BattleFixture.Duel(
      hitChanceCalculator: new AlwaysHitCalculator(),
      player: new("Alpha", Weapon: TestData.MakeStatusWeapon(burn, damage: 3, element: Element.Kinetic)),
      enemy: new(
        "Hostile",
        Weapon: TestData.MakeWeapon("Enemy Rifle"),
        Armor: TestData.MakeArmor("Thermal Plating", armor: 10, element: Element.Thermal)));
    var target = battle.EnemyUnit;
    battle.Attack(battle.PlayerUnit, battle.EnemyUnit);   // 3 Kinetic absorbed at 1x: armor 10 -> 7
    var armor = target.EquippedArmor.RequireSome().Capability;
    Assert.Equal(7, armor.Current);
    Assert.Equal(target.MaxHealth, target.CurrentHealth);

    battle.EndFactionTurn(battle.PlayerFaction);
    battle.EndFactionTurn(battle.EnemyFaction);           // tick 2 Thermal vs Thermal armor: 1.5x -> 3 armor

    Assert.Equal(4, armor.Current);
    Assert.Equal(target.MaxHealth, target.CurrentHealth);
  }

  [TestCase(TestName = "An active DoT suppresses armor regen by re-arming its delay")]
  public void DotSuppressesArmorRegen()
  {
    var burn = TestData.MakeBurn();
    using var battle = BattleFixture.Duel(
      hitChanceCalculator: new AlwaysHitCalculator(),
      player: new("Alpha", Weapon: TestData.MakeStatusWeapon(burn, damage: 3, element: Element.Thermal)),
      enemy: new(
        "Hostile",
        Weapon: TestData.MakeWeapon("Enemy Rifle"),
        Armor: TestData.MakeArmor("Recharger", armor: 10, element: Element.Kinetic, regenDelayTurns: 1, regenPerTurn: 3)));
    var target = battle.EnemyUnit;
    battle.Attack(battle.PlayerUnit, battle.EnemyUnit);   // 3 Thermal vs Kinetic armor at 1x: armor 10 -> 7, delay armed
    var armor = target.EquippedArmor.RequireSome().Capability;
    Assert.Equal(7, armor.Current);

    battle.ClearEvents();

    battle.EndFactionTurn(battle.PlayerFaction);
    battle.EndFactionTurn(battle.EnemyFaction);           // tick: armor 7 -> 5 and delay re-armed before regen runs
    Assert.Equal(5, armor.Current);

    battle.EndFactionTurn(battle.PlayerFaction);
    battle.EndFactionTurn(battle.EnemyFaction);           // tick: armor 5 -> 3, burn expires; regen still suppressed
    Assert.Equal(3, armor.Current);
    Assert.False(battle.Events.EventsOf<UnitArmorRegeneratedBattleEvent>().AsValueEnumerable().Any());

    battle.EndFactionTurn(battle.PlayerFaction);
    battle.EndFactionTurn(battle.EnemyFaction);           // burn gone: delay already counted down, regen restores 3
    Assert.Equal(6, armor.Current);
    Assert.True(battle.Events.EventsOf<UnitArmorRegeneratedBattleEvent>().AsValueEnumerable().Any());
  }

  [TestCase(TestName = "A lethal tick kills the unit and the turn advances cleanly")]
  public void LethalTickKillsAndTurnAdvances()
  {
    var burn = TestData.MakeBurn(duration: 3);
    using var battle = BattleFixture.Duel(
      hitChanceCalculator: new AlwaysHitCalculator(),
      player: new("Alpha", Weapon: TestData.MakeStatusWeapon(burn, damage: 3)),
      enemy: new("Hostile", Health: 4, Weapon: TestData.MakeWeapon("Enemy Rifle")));
    var target = battle.EnemyUnit;
    battle.Attack(battle.PlayerUnit, battle.EnemyUnit);   // 4 -> 1 health, burning
    Assert.True(target.IsAlive);

    battle.EndFactionTurn(battle.PlayerFaction);
    battle.ClearEvents();
    battle.EndFactionTurn(battle.EnemyFaction);           // tick 2 kills at the enemy's own turn end

    Assert.True(target.IsDead);
    Assert.True(battle.Events.EventsOf<UnitKilledBattleEvent>().AsValueEnumerable().Any());
    Assert.Equal(BattlePhase.InProgress, battle.Session.Phase);
    Assert.Equal(battle.PlayerFaction, battle.Session.ActiveSide);
  }

  [TestCase(TestName = "A stunned unit loses its turn and recovers after its own turn end")]
  public void StunnedUnitLosesTurnAndRecovers()
  {
    var stun = TestData.MakeStun();
    using var battle = BattleFixture.Duel(
      hitChanceCalculator: new AlwaysHitCalculator(),
      player: new("Alpha", Weapon: TestData.MakeStatusWeapon(stun)),
      enemy: new("Hostile", Weapon: TestData.MakeWeapon("Enemy Rifle")));
    var target = battle.EnemyUnit;
    battle.Attack(battle.PlayerUnit, battle.EnemyUnit);
    Assert.True(target.IsImmobilized);

    battle.EndFactionTurn(battle.PlayerFaction);          // enemy turn: stunned target cannot act
    Assert.False(battle.Session.CanUnitActNow(target));

    battle.ClearEvents();
    battle.EndFactionTurn(battle.EnemyFaction);           // stun ticks 1 -> 0 and expires

    Assert.False(target.IsImmobilized);
    Assert.True(battle.Events.EventsOf<UnitStatusEffectExpiredBattleEvent>().AsValueEnumerable().Any());
  }

  [TestCase(TestName = "A lethal tick stops the unit's remaining statuses")]
  public void LethalTickStopsRemainingStatuses()
  {
    using var battle = BattleFixture.Duel(
      hitChanceCalculator: new AlwaysHitCalculator(),
      player: new("Alpha", Weapon: TestData.MakeWeapon("Rifle")),
      enemy: new("Hostile", Health: 2, Weapon: TestData.MakeWeapon("Enemy Rifle")));
    var target = battle.EnemyUnit;
    // Both lethal, so the assertion holds regardless of status iteration order.
    var burn = TestData.MakeBurn(tickDamage: 5);
    var acid = TestData.MakeBurn(tickDamage: 5, tickElement: Element.Chem, name: "Acid");
    target.ApplyStatusEffect(burn);
    target.ApplyStatusEffect(acid);

    battle.EndFactionTurn(battle.PlayerFaction);
    battle.ClearEvents();
    battle.EndFactionTurn(battle.EnemyFaction);           // first tick kills; the second status never ticks

    Assert.True(target.IsDead);
    Assert.Equal(1, battle.Events.EventsOf<UnitStatusEffectTickedBattleEvent>().AsValueEnumerable().Count());
    Assert.True(battle.Events.EventsOf<UnitKilledBattleEvent>().AsValueEnumerable().Any());
    Assert.False(battle.Events.EventsOf<UnitStatusEffectExpiredBattleEvent>().AsValueEnumerable().Any());
    Assert.Equal(2, target.ActiveStatusEffects.Count);
    Assert.Equal(1, target.ActiveStatusEffects.AsValueEnumerable().Count(effect => effect.RemainingTurns == 1));
    Assert.Equal(1, target.ActiveStatusEffects.AsValueEnumerable().Count(effect => effect.RemainingTurns == 2));
    Assert.Equal(BattlePhase.InProgress, battle.Session.Phase);
    Assert.Equal(battle.PlayerFaction, battle.Session.ActiveSide);
  }

  [TestCase(TestName = "Multiple statuses on one unit each tick at the owner's turn end")]
  public void MultipleStatusesEachTick()
  {
    using var battle = BattleFixture.Duel(
      hitChanceCalculator: new AlwaysHitCalculator(),
      player: new("Alpha", Weapon: TestData.MakeWeapon("Rifle")),
      enemy: new("Hostile", Health: 20, Weapon: TestData.MakeWeapon("Enemy Rifle")));
    var target = battle.EnemyUnit;
    var burn = TestData.MakeBurn();
    var poison = TestData.MakeBurn(duration: 3, tickDamage: 1, tickElement: Element.Chem, name: "Poison");
    target.ApplyStatusEffect(burn);
    target.ApplyStatusEffect(poison);

    battle.EndFactionTurn(battle.PlayerFaction);
    battle.ClearEvents();
    battle.EndFactionTurn(battle.EnemyFaction);           // both tick: 2 + 1 damage

    Assert.Equal(17, target.CurrentHealth);
    Assert.Equal(2, battle.Events.EventsOf<UnitStatusEffectTickedBattleEvent>().AsValueEnumerable().Count());
    Assert.Equal(2, target.ActiveStatusEffects.Count);
    Assert.Equal(1, target.ActiveStatusEffects.AsValueEnumerable().Count(effect => effect.RemainingTurns == 1));
    Assert.Equal(1, target.ActiveStatusEffects.AsValueEnumerable().Count(effect => effect.RemainingTurns == 2));
  }
}
