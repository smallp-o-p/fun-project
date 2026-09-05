using System;
using FunProject.Battle;
using FunProject.Buffs;
using FunProject.Stats;
using FunProject.Weapons;
using GdUnit4;
using Godot;

[TestSuite]
[RequireGodotRuntime]
public partial class BuffUnitStateTest
{
  private static Buff AimBuff(string name = "Focus") =>
    MakeBuff(
      name,
      new HealthBelowPercentCondition { Percent = 50f },
      statMods: [new AimStatMod { Modifiers = [StatModifier.Add(10)] }]);

  [TestCase(TestName = "Unit aggregates innate and equipped-item buffs; identical buffs stack")]
  public void AggregatesAndStacks()
  {
    var faction = MakeFaction("Player");
    var session = MakeSession(new Vector3I(8, 1, 8), [faction]);
    var shared = AimBuff("Shared");
    var innateOnly = AimBuff("InnateOnly");

    var unit = SpawnUnit(
      session,
      MakeCombatant("Alpha", faction, buffs: [shared, innateOnly]),
      new Vector3I(4, 0, 4),
      MakeWeapon("Charm Blade", grantedBuffs: [shared]));

    Assert.Equal(3, unit.State.Buffs.Count);
    Assert.Equal(2, unit.State.Buffs.AsValueEnumerable().Count(buff => buff == shared));
    Assert.Equal(shared, unit.State.Buffs[0]);
    Assert.Equal(innateOnly, unit.State.Buffs[1]);
    Assert.Equal(shared, unit.State.Buffs[2]);
  }

  [TestCase(TestName = "Active buff StatMods flow into EffectiveStat; inactive contribute nothing")]
  public void ActiveBuffContributesStats()
  {
    var faction = MakeFaction("Player");
    var session = MakeSession(new Vector3I(8, 1, 8), [faction]);
    var buffed = SpawnUnit(
      session,
      MakeCombatant("Alpha", faction, health: 20, aim: 65, buffs: [AimBuff()]),
      new Vector3I(4, 0, 4));

    Assert.Equal(65f, buffed.State.EffectiveStat<AimStat>());
    Assert.False(buffed.State.ActiveBuffDamageMods.AsValueEnumerable().Any());

    ApplyDamage(session, buffed.State, 11); // 9/20: condition holds
    buffed.State.EvaluateBuffs(session);

    Assert.Equal(75f, buffed.State.EffectiveStat<AimStat>());
    Assert.Equal(1, buffed.State.ActiveBuffs.AsValueEnumerable().Count());
  }

  [TestCase(TestName = "ClampCurrentHealthToMax lowers current health to a reduced max and never raises it")]
  public void ClampLowersToReducedMax()
  {
    var faction = MakeFaction("Player");
    var session = MakeSession(new Vector3I(8, 1, 8), [faction]);
    // Percent 200 => current < 2*max is always true: an always-on buff without board setup.
    var intimidation = MakeBuff(
      "Intimidation",
      new HealthBelowPercentCondition { Percent = 200f },
      statMods: [new HealthStatMod { Modifiers = [StatModifier.Add(-5)] }]);
    var unit = SpawnUnit(
      session,
      MakeCombatant("Alpha", faction, health: 20, buffs: [intimidation]),
      new Vector3I(4, 0, 4));

    unit.State.ClampCurrentHealthToMax();

    Assert.Equal(15, unit.State.MaxHealth);
    Assert.Equal(15, unit.State.CurrentHealth);

    unit.State.ClampCurrentHealthToMax(); // idempotent; never raises
    Assert.Equal(15, unit.State.CurrentHealth);
  }

  [TestCase(TestName = "A buff that would push max health to zero clamps current health to 1, never killing")]
  public void ClampFloorsAtOneHealth()
  {
    var faction = MakeFaction("Player");
    var session = MakeSession(new Vector3I(8, 1, 8), [faction]);
    var crushing = MakeBuff(
      "Crushing Doubt",
      new HealthBelowPercentCondition { Percent = 200f },
      statMods: [new HealthStatMod { Modifiers = [StatModifier.Add(-25)] }]);
    var unit = SpawnUnit(
      session,
      MakeCombatant("Alpha", faction, health: 20, buffs: [crushing]),
      new Vector3I(4, 0, 4));

    Assert.True(unit.State.IsAlive);
    Assert.Equal(1, unit.State.CurrentHealth);
  }

  [TestCase(TestName = "Sharing a buff resource preserves independent unit activation snapshots")]
  public void SharedResourceHasIndependentActivation()
  {
    var shared = AimBuff("Shared");
    var battle = new BattleDuelBuilder
    {
      Player = new DuelSide("Alpha", Buffs: [shared]),
      Enemy = new DuelSide("Hostile", Buffs: [shared]),
    }.Start();
    var player = battle.PlayerUnit.State;
    var enemy = battle.EnemyUnit.State;
    var before = player.ActiveBuffs;
    var recorder = new BattleEventRecorder(battle.Session);

    Assert.True(ReferenceEquals(shared, player.Buffs[0]));
    Assert.True(ReferenceEquals(shared, enemy.Buffs[0]));
    ApplyDamage(battle.Session, player, 11);
    Assert.Equal(0, player.ActiveBuffs.AsValueEnumerable().Count());
    Assert.Equal(65f, player.EffectiveStat<AimStat>());

    EndFactionTurn(battle.Executor, battle.PlayerFaction);
    Assert.Equal(1, player.ActiveBuffs.AsValueEnumerable().Count());
    Assert.Equal(0, enemy.ActiveBuffs.AsValueEnumerable().Count());
    Assert.Equal(0, before.AsValueEnumerable().Count());
    Assert.Equal(75f, player.EffectiveStat<AimStat>());
    Assert.Equal(65f, enemy.EffectiveStat<AimStat>());
    Assert.Equal(player, recorder.Single<UnitBuffActivatedBattleEvent>().Unit);

    EndFactionTurn(battle.Executor, battle.EnemyFaction);
    Assert.Equal(1, recorder.OfType<UnitBuffActivatedBattleEvent>().AsValueEnumerable().Count());
  }

  [TestCase(TestName = "Duplicate grants keep separate flags and evaluate after each health clamp")]
  public void DuplicateGrantsEvaluateSequentially()
  {
    var shared = MakeBuff(
      "Fragile Focus",
      new HealthBelowPercentCondition { Percent = 75f },
      statMods:
      [
        new HealthStatMod { Modifiers = [StatModifier.Add(-10)] },
        new AimStatMod { Modifiers = [StatModifier.Add(10)] },
      ]);
    var battle = new BattleDuelBuilder
    {
      Player = new DuelSide("Alpha", Buffs: [shared], Weapon: MakeWeapon("Charm", grantedBuffs: [shared])),
      Enemy = new DuelSide("Hostile"),
    }.Start();
    var player = battle.PlayerUnit.State;
    var recorder = new BattleEventRecorder(battle.Session);

    Assert.Equal(2, player.Buffs.Count);
    Assert.True(ReferenceEquals(shared, player.Buffs[0]));
    Assert.True(ReferenceEquals(shared, player.Buffs[1]));
    ApplyDamage(battle.Session, player, 6); // 14/20: first grant can activate.
    EndFactionTurn(battle.Executor, battle.PlayerFaction);

    // First activation lowers max to 10 and clamps health to 10. The second
    // grant sees 10/10, fails the 75% threshold, and remains inactive.
    Assert.Equal(1, player.ActiveBuffs.AsValueEnumerable().Count());
    Assert.Equal(10, player.MaxHealth);
    Assert.Equal(10, player.CurrentHealth);
    Assert.Equal(75f, player.EffectiveStat<AimStat>());
    Assert.Equal(shared, recorder.Single<UnitBuffActivatedBattleEvent>().Buff);

    EndFactionTurn(battle.Executor, battle.EnemyFaction);
    // First deactivates at 10/10, restoring max 20 without healing. The second
    // now sees 10/20 and activates. A resource-keyed flag cannot represent this.
    Assert.Equal(1, player.ActiveBuffs.AsValueEnumerable().Count());
    Assert.Equal(10, player.MaxHealth);
    Assert.Equal(10, player.CurrentHealth);
    Assert.Equal(75f, player.EffectiveStat<AimStat>());
    var flips = recorder.All.AsValueEnumerable()
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
    var shared = MakeBuff(
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
    var battle = new BattleDuelBuilder
    {
      HitChanceCalculator = new AlwaysHitCalculator(),
      Player = new DuelSide("Alpha", Buffs: [shared], Weapon: MakeWeapon("Charm", damage: 5, grantedBuffs: [shared])),
      Enemy = new DuelSide("Hostile", Health: 30),
    }.Start();
    var player = battle.PlayerUnit.State;
    var recorder = new BattleEventRecorder(battle.Session);

    ApplyDamage(battle.Session, player, 11);
    EndFactionTurn(battle.Executor, battle.PlayerFaction);
    Assert.Equal(2, player.ActiveBuffs.AsValueEnumerable().Count());
    Assert.Equal(85f, player.EffectiveStat<AimStat>());
    Assert.Equal(2, recorder.OfType<UnitBuffActivatedBattleEvent>().AsValueEnumerable().Count());

    EndFactionTurn(battle.Executor, battle.EnemyFaction);
    Attack(battle.Session, battle.Executor, battle.PlayerUnit, battle.EnemyUnit);
    Assert.Equal(19, battle.EnemyUnit.State.CurrentHealth); // 30 - (5 + 3 + 3)
    Assert.Equal(2, recorder.OfType<UnitBuffActivatedBattleEvent>().AsValueEnumerable().Count());
  }

  [TestCase(TestName = "A missing authored condition fails at unit construction")]
  public void UnitRequiresBuffCondition()
  {
    var faction = MakeFaction("Player");
    var session = MakeSession(new Vector3I(8, 1, 8), [faction]);
    var broken = MakeBuff("Broken", null);
    var combatant = MakeCombatant("Alpha", faction, buffs: [broken]);

    Assert.Throws<InvalidOperationException>(() =>
      SpawnUnit(session, combatant, new Vector3I(4, 0, 4)));
  }

  [TestCase(TestName = "A null buff grant fails at unit construction")]
  public void UnitRejectsNullBuffGrant()
  {
    var faction = MakeFaction("Player");
    var session = MakeSession(new Vector3I(8, 1, 8), [faction]);
    var combatant = MakeCombatant("Alpha", faction, buffs: [null]);

    Assert.Throws<ArgumentNullException>(() =>
      SpawnUnit(session, combatant, new Vector3I(4, 0, 4)));
  }
}
