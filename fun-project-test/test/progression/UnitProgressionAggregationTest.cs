using System;
using FunProject.Buffs;
using FunProject.Progression;
using FunProject.Stats;
using GdUnit4;

[TestSuite]
[RequireGodotRuntime]
public partial class UnitProgressionAggregationTest
{
  [TestCase(TestName = "Aggregation covers only unlocked steps across committed paths")]
  public void AggregatesUnlockedOnly()
  {
    var progression = new UnitProgression();
    var aimMod = new AimStatMod { Modifiers = [StatModifier.Add(5)] };
    var healthMod = new HealthStatMod { Modifiers = [StatModifier.Add(10)] };
    var burn = TestData.MakeBuff("Burn", new HealthBelowPercentCondition { Percent = 50f });
    var scout = TestData.MakePath("Scout",
      TestData.MakeStep(1, TestData.MakeStatModEffect(aimMod)),
      TestData.MakeStep(1, TestData.MakeAbilityGrantEffect("Sprint")));
    var guardian = TestData.MakePath("Guardian",
      TestData.MakeStep(1, TestData.MakeStatModEffect(healthMod), TestData.MakeBuffGrantEffect(burn)));

    progression.AwardPoints(3);
    progression.TryCommit(scout);
    progression.TryCommit(guardian);

    progression.TryUnlockNext(scout).RequireSome(); // scout step 1 only
    Assert.Equal(1, progression.StatMods().Count);
    Assert.Equal(aimMod, progression.StatMods()[0]);
    Assert.Equal(0, progression.GrantedBuffs().Count);
    Assert.Equal(0, progression.GrantedAbilities().Count);

    progression.TryUnlockNext(guardian).RequireSome(); // guardian step 1
    Assert.Equal(2, progression.StatMods().Count);
    Assert.Equal(1, progression.GrantedBuffs().Count);
    Assert.Equal(burn, progression.GrantedBuffs()[0]);
    Assert.Equal(0, progression.GrantedAbilities().Count);

    progression.AwardPoints(1);
    progression.TryUnlockNext(scout).RequireSome(); // scout step 2: the ability
    Assert.Equal("Sprint", progression.GrantedAbilities()[0].Name);
  }

  [TestCase(TestName = "An ability grant with no assigned Ability throws at aggregation (authoring mistake)")]
  public void NullAbilityThrows()
  {
    var progression = new UnitProgression();
    // Godot bypass shape: no Ability set
    var path = TestData.MakePath("Broken", TestData.MakeStep(1, new AbilityGrantUpgradeEffectData { Ability = null }));
    progression.AwardPoints(1);
    progression.TryCommit(path);
    progression.TryUnlockNext(path).RequireSome();

    Assert.Throws<InvalidOperationException>(() => progression.GrantedAbilities());
  }
}
