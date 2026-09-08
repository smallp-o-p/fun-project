using FunProject.Battle;
using FunProject.Buffs;
using FunProject.Stats;
using FunProject.Weapons;
using GdUnit4;

[TestSuite]
[RequireGodotRuntime]
public partial class BuffUnitStateTest
{
  private static Buff AimBuff(string name = "Focus") =>
    TestData.MakeBuff(
      name,
      new HealthBelowPercentCondition { Percent = 50f },
      statMods: [new AimStatMod { Modifiers = [StatModifier.Add(10)] }]);

  [TestCase(TestName = "Unit aggregates innate and equipped-item buffs; identical buffs stack")]
  public void AggregatesAndStacks()
  {
    var faction = TestData.MakeFaction("Player");
    using var battle = new BattleFixture(new Vector3I(8, 1, 8), [faction]);
    var shared = AimBuff("Shared");
    var innateOnly = AimBuff("InnateOnly");

    var unit = battle.Spawn(
      TestData.MakeCombatant("Alpha", faction, buffs: [shared, innateOnly]),
      new Vector3I(4, 0, 4),
      TestData.MakeWeapon("Charm Blade", grantedBuffs: [shared]));

    Assert.Equal(3, unit.Buffs.Count);
    Assert.Equal(2, unit.Buffs.AsValueEnumerable().Count(buff => buff == shared));
    Assert.Equal(shared, unit.Buffs[0]);
    Assert.Equal(innateOnly, unit.Buffs[1]);
    Assert.Equal(shared, unit.Buffs[2]);
  }

  [TestCase(TestName = "Active buff StatMods flow into EffectiveStat; inactive contribute nothing")]
  public void ActiveBuffContributesStats()
  {
    var faction = TestData.MakeFaction("Player");
    using var battle = new BattleFixture(new Vector3I(8, 1, 8), [faction]);
    var buffed = battle.Spawn(
      TestData.MakeCombatant("Alpha", faction, health: 20, aim: 65, buffs: [AimBuff()]),
      new Vector3I(4, 0, 4));

    Assert.Equal(65f, buffed.EffectiveStat<AimStat>());
    Assert.False(buffed.ActiveBuffDamageMods.AsValueEnumerable().Any());

    battle.ApplyDamage(buffed, 11); // 9/20: condition holds
    buffed.EvaluateBuffs(battle.Session);

    Assert.Equal(75f, buffed.EffectiveStat<AimStat>());
    Assert.Equal(1, buffed.ActiveBuffs.AsValueEnumerable().Count());
  }

  [TestCase(TestName = "ClampCurrentHealthToMax lowers current health to a reduced max and never raises it")]
  public void ClampLowersToReducedMax()
  {
    var faction = TestData.MakeFaction("Player");
    using var battle = new BattleFixture(new Vector3I(8, 1, 8), [faction]);
    // Percent 200 => current < 2*max is always true: an always-on buff without board setup.
    var intimidation = TestData.MakeBuff(
      "Intimidation",
      new HealthBelowPercentCondition { Percent = 200f },
      statMods: [new HealthStatMod { Modifiers = [StatModifier.Add(-5)] }]);
    var unit = battle.Spawn(
      TestData.MakeCombatant("Alpha", faction, health: 20, buffs: [intimidation]),
      new Vector3I(4, 0, 4));

    unit.ClampCurrentHealthToMax();

    Assert.Equal(15, unit.MaxHealth);
    Assert.Equal(15, unit.CurrentHealth);

    unit.ClampCurrentHealthToMax(); // idempotent; never raises
    Assert.Equal(15, unit.CurrentHealth);
  }

  [TestCase(TestName = "A buff that would push max health to zero clamps current health to 1, never killing")]
  public void ClampFloorsAtOneHealth()
  {
    var faction = TestData.MakeFaction("Player");
    using var battle = new BattleFixture(new Vector3I(8, 1, 8), [faction]);
    var crushing = TestData.MakeBuff(
      "Crushing Doubt",
      new HealthBelowPercentCondition { Percent = 200f },
      statMods: [new HealthStatMod { Modifiers = [StatModifier.Add(-25)] }]);
    var unit = battle.Spawn(
      TestData.MakeCombatant("Alpha", faction, health: 20, buffs: [crushing]),
      new Vector3I(4, 0, 4));

    Assert.True(unit.IsAlive);
    Assert.Equal(1, unit.CurrentHealth);
  }

  [TestCase(TestName = "Sharing a buff resource preserves independent unit activation snapshots")]
  public void SharedResourceHasIndependentActivation()
  {
    var shared = AimBuff("Shared");
    using var battle = BattleFixture.Duel(
      player: new("Alpha", Buffs: [shared]),
      enemy: new("Hostile", Buffs: [shared]));
    var player = battle.PlayerUnit;
    var enemy = battle.EnemyUnit;
    var before = player.ActiveBuffs;
    battle.ClearEvents();

    Assert.True(ReferenceEquals(shared, player.Buffs[0]));
    Assert.True(ReferenceEquals(shared, enemy.Buffs[0]));
    battle.ApplyDamage(player, 11);
    Assert.Equal(0, player.ActiveBuffs.AsValueEnumerable().Count());
    Assert.Equal(65f, player.EffectiveStat<AimStat>());

    battle.EndFactionTurn(battle.PlayerFaction);
    Assert.Equal(1, player.ActiveBuffs.AsValueEnumerable().Count());
    Assert.Equal(0, enemy.ActiveBuffs.AsValueEnumerable().Count());
    Assert.Equal(0, before.AsValueEnumerable().Count());
    Assert.Equal(75f, player.EffectiveStat<AimStat>());
    Assert.Equal(65f, enemy.EffectiveStat<AimStat>());
    Assert.Equal(player, battle.Events.SingleEvent<UnitBuffActivatedBattleEvent>().Unit);

    battle.EndFactionTurn(battle.EnemyFaction);
    Assert.Equal(1, battle.Events.EventsOf<UnitBuffActivatedBattleEvent>().AsValueEnumerable().Count());
  }

  [TestCase(TestName = "Duplicate grants keep separate flags and evaluate after each health clamp")]
  public void DuplicateGrantsEvaluateSequentially()
  {
    var shared = TestData.MakeBuff(
      "Fragile Focus",
      new HealthBelowPercentCondition { Percent = 75f },
      statMods:
      [
        new HealthStatMod { Modifiers = [StatModifier.Add(-10)] },
        new AimStatMod { Modifiers = [StatModifier.Add(10)] },
      ]);
    using var battle = BattleFixture.Duel(
      player: new("Alpha", Buffs: [shared], Weapon: TestData.MakeWeapon("Charm", grantedBuffs: [shared])));
    var player = battle.PlayerUnit;
    battle.ClearEvents();

    Assert.Equal(2, player.Buffs.Count);
    Assert.True(ReferenceEquals(shared, player.Buffs[0]));
    Assert.True(ReferenceEquals(shared, player.Buffs[1]));
    battle.ApplyDamage(player, 6); // 14/20: first grant can activate.
    battle.EndFactionTurn(battle.PlayerFaction);

    // First activation lowers max to 10 and clamps health to 10. The second
    // grant sees 10/10, fails the 75% threshold, and remains inactive.
    Assert.Equal(1, player.ActiveBuffs.AsValueEnumerable().Count());
    Assert.Equal(10, player.MaxHealth);
    Assert.Equal(10, player.CurrentHealth);
    Assert.Equal(75f, player.EffectiveStat<AimStat>());
    Assert.Equal(shared, battle.Events.SingleEvent<UnitBuffActivatedBattleEvent>().Buff);

    battle.EndFactionTurn(battle.EnemyFaction);
    // First deactivates at 10/10, restoring max 20 without healing. The second
    // now sees 10/20 and activates. A resource-keyed flag cannot represent this.
    Assert.Equal(1, player.ActiveBuffs.AsValueEnumerable().Count());
    Assert.Equal(10, player.MaxHealth);
    Assert.Equal(10, player.CurrentHealth);
    Assert.Equal(75f, player.EffectiveStat<AimStat>());
    var flips = battle.Events.AsValueEnumerable()
      .Where(evt => evt is UnitBuffActivatedBattleEvent or UnitBuffDeactivatedBattleEvent)
      .ToArray();
    Assert.Equal(3, flips.Length);
    Assert.True(flips[0] is UnitBuffActivatedBattleEvent);
    Assert.True(flips[1] is UnitBuffDeactivatedBattleEvent);
    Assert.True(flips[2] is UnitBuffActivatedBattleEvent);
    Assert.Equal(shared, ((UnitBuffDeactivatedBattleEvent)flips[1]).Buff);
    Assert.Equal(shared, ((UnitBuffActivatedBattleEvent)flips[2]).Buff);
  }

  [TestCase(TestName = "Duplicate active grants both contribute stats and attack damage")]
  public void DuplicateGrantsContributeStatsAndDamage()
  {
    var shared = TestData.MakeBuff(
      "Stacked Focus",
      new HealthBelowPercentCondition { Percent = 50f },
      statMods: [new AimStatMod { Modifiers = [StatModifier.Add(10)] }],
      damageMods:
      [
        new DamageBundleMod
        {
          PacketModifiers = [new PacketModifier { AffectAllElements = true, Ops = [StatModifier.Add(3)] }],
        },
      ]);
    using var battle = BattleFixture.Duel(
      hitChanceCalculator: new AlwaysHitCalculator(),
      player: new("Alpha", Buffs: [shared], Weapon: TestData.MakeWeapon("Charm", damage: 5, grantedBuffs: [shared])),
      enemy: new("Hostile", Health: 30));
    var player = battle.PlayerUnit;
    battle.ClearEvents();

    battle.ApplyDamage(player, 11);
    battle.EndFactionTurn(battle.PlayerFaction);
    Assert.Equal(2, player.ActiveBuffs.AsValueEnumerable().Count());
    Assert.Equal(85f, player.EffectiveStat<AimStat>());
    Assert.Equal(2, battle.Events.EventsOf<UnitBuffActivatedBattleEvent>().AsValueEnumerable().Count());

    battle.EndFactionTurn(battle.EnemyFaction);
    battle.Attack(battle.PlayerUnit, battle.EnemyUnit);
    Assert.Equal(19, battle.EnemyUnit.CurrentHealth); // 30 - (5 + 3 + 3)
    Assert.Equal(2, battle.Events.EventsOf<UnitBuffActivatedBattleEvent>().AsValueEnumerable().Count());
  }

  [TestCase(TestName = "A missing authored condition fails at unit construction")]
  public void UnitRequiresBuffCondition()
  {
    var faction = TestData.MakeFaction("Player");
    using var battle = new BattleFixture(new Vector3I(8, 1, 8), [faction]);
    var broken = TestData.MakeBuff("Broken", null);
    var combatant = TestData.MakeCombatant("Alpha", faction, buffs: [broken]);

    Assert.Throws<InvalidOperationException>(() =>
      battle.Spawn(combatant, new Vector3I(4, 0, 4)));
  }

  [TestCase(TestName = "A null buff grant fails at unit construction")]
  public void UnitRejectsNullBuffGrant()
  {
    var faction = TestData.MakeFaction("Player");
    using var battle = new BattleFixture(new Vector3I(8, 1, 8), [faction]);
    var combatant = TestData.MakeCombatant("Alpha", faction, buffs: [null]);

    Assert.Throws<ArgumentNullException>(() =>
      battle.Spawn(combatant, new Vector3I(4, 0, 4)));
  }
}
