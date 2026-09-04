using FunProject.Buffs;
using FunProject.Stats;
using GdUnit4;
using Godot;

[TestSuite]
[RequireGodotRuntime]
public partial class BuffUnitStateTest
{
  private static BuffData AimBuff(string name = "Focus") =>
    MakeBuff(
      name,
      new HealthBelowPercentConditionData { Percent = 50f },
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
    Assert.Equal(2, unit.State.Buffs.AsValueEnumerable().Count(buff => buff.Data == shared));
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
    Assert.True(buffed.State.Buffs[0].Evaluate(session, buffed.State));

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
      new HealthBelowPercentConditionData { Percent = 200f },
      statMods: [new HealthStatMod { Modifiers = [StatModifier.Add(-5)] }]);
    var unit = SpawnUnit(
      session,
      MakeCombatant("Alpha", faction, health: 20, buffs: [intimidation]),
      new Vector3I(4, 0, 4));

    // Note: once Task 5 lands, spawn-time evaluation will have already activated this buff
    // and clamped health inside SpawnUnit. Evaluate/clamp here directly so this test is
    // stable both before and after Task 5 (both are no-ops when already applied).
    unit.State.Buffs[0].Evaluate(session, unit.State);
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
      new HealthBelowPercentConditionData { Percent = 200f },
      statMods: [new HealthStatMod { Modifiers = [StatModifier.Add(-25)] }]);
    var unit = SpawnUnit(
      session,
      MakeCombatant("Alpha", faction, health: 20, buffs: [crushing]),
      new Vector3I(4, 0, 4));

    Assert.True(unit.State.IsAlive);
    Assert.Equal(1, unit.State.CurrentHealth);
  }
}
