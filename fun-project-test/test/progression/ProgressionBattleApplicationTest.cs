using FunProject.Buffs;
using FunProject.Stats;
using GdUnit4;
using Godot;

[TestSuite]
[RequireGodotRuntime]
public partial class ProgressionBattleApplicationTest
{
  [TestCase(TestName = "Unlocked stat-mod step raises the spawned unit's effective aim")]
  public void UnlockedStatModAppliesAtSpawn()
  {
    var faction = TestData.MakeFaction("Player");
    using var battle = new BattleFixture(new Vector3I(8, 1, 8), [faction]);
    var combatant = TestData.MakeCombatant("Alpha", faction, aim: 65);
    battle.UnlockFirstStep(combatant, TestData.MakePath("Marksman",
      TestData.MakeStep(1, TestData.MakeStatModEffect(new AimStatMod { Modifiers = [StatModifier.Add(10)] }))));

    var unit = battle.Spawn(combatant, new Vector3I(4, 0, 4));
    Assert.Equal(75f, unit.EffectiveStat<AimStat>());
  }

  [TestCase(TestName = "Unlocked health step raises spawn MaxHealth and CurrentHealth")]
  public void UnlockedHealthModRaisesSpawnHealth()
  {
    var faction = TestData.MakeFaction("Player");
    using var battle = new BattleFixture(new Vector3I(8, 1, 8), [faction]);
    var combatant = TestData.MakeCombatant("Bravo", faction, health: 20);
    battle.UnlockFirstStep(combatant, TestData.MakePath("Vitality",
      TestData.MakeStep(1, TestData.MakeStatModEffect(new HealthStatMod { Modifiers = [StatModifier.Add(5)] }))));

    var unit = battle.Spawn(combatant, new Vector3I(4, 0, 4));
    Assert.Equal(25, unit.MaxHealth);
    Assert.Equal(25, unit.CurrentHealth);
  }

  [TestCase(TestName = "Unlocked buff grants surface via InnateBuffs and stack with innate grants")]
  public void UnlockedBuffGrantSurfacesAndStacks()
  {
    var faction = TestData.MakeFaction("Player");
    using var battle = new BattleFixture(new Vector3I(8, 1, 8), [faction]);
    var condition = new HealthBelowPercentCondition { Percent = 50f };
    var shared = TestData.MakeBuff("Shared", condition);
    var innateOnly = TestData.MakeBuff("Innate", condition);
    var combatant = TestData.MakeCombatant("Charlie", faction, buffs: [shared, innateOnly]);

    Assert.Equal(2, combatant.InnateBuffs.Count); // authored innate only, before training

    battle.UnlockFirstStep(combatant, TestData.MakePath("Guardian",
      TestData.MakeStep(1, TestData.MakeBuffGrantEffect(shared, TestData.MakeBuff("Learned", condition)))));

    // InnateBuffs is the single surface: authored innate ++ trained grants, live-computed.
    Assert.Equal(4, combatant.InnateBuffs.Count); // shared, innateOnly, shared, learned
    Assert.Equal(2, combatant.InnateBuffs.AsValueEnumerable().Count(buff => buff == shared));

    var unit = battle.Spawn(combatant, new Vector3I(4, 0, 4));
    Assert.Equal(4, unit.Buffs.Count); // identical buffs stack, one entry per grant
    Assert.Equal(2, unit.Buffs.AsValueEnumerable().Count(buff => buff == shared));
  }
}
