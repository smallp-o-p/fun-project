#nullable disable warnings
using FunProject.Buffs;
using FunProject.Combatants;
using FunProject.Progression;
using FunProject.Stats;
using GdUnit4;
using Godot;

[TestSuite]
[RequireGodotRuntime]
public partial class RankEffectsAggregationTest
{
  [TestCase(TestName = "Combatants without a bespoke rank table use the authored default ladder")]
  public void UsesAuthoredDefaultRankTable()
  {
    var data = TestData.MakeCombatantData("Alpha");
    data.RankTable = null;
    var combatant = new Combatant(data, TestData.MakeFaction("Player"));

    Assert.Equal("Rookie", combatant.Rank.RankName);
    combatant.Rank.Gain(100);
    Assert.Equal("Squaddie", combatant.Rank.RankName);
    Assert.Equal(0, combatant.Rank.Xp);
  }

  [TestCase(TestName = "Held rung stat mods are cumulative and raise the spawned unit's effective aim")]
  public void RungStatModsAreCumulative()
  {
    var faction = TestData.MakeFaction("Player");
    using var battle = new BattleFixture(new Vector3I(8, 1, 8), [faction]);
    var table = TestData.MakeRankTable(
      new RankLevelData { Name = "Rookie", GainFactorPercent = 100 },
      new RankLevelData
      {
        Name = "Squaddie",
        GainFactorPercent = 100,
        Effects = [TestData.MakeStatModEffect(new AimStatMod { Modifiers = [StatModifier.Add(5)] })],
      },
      new RankLevelData
      {
        Name = "Corporal",
        GainFactorPercent = 100,
        Effects = [TestData.MakeStatModEffect(new AimStatMod { Modifiers = [StatModifier.Add(10)] })],
      });
    var combatant = TestData.MakeCombatant("Alpha", faction,
      health: 10, actionPoints: 2, movement: 8, rankTable: table);

    var unit = battle.Spawn(combatant, new Vector3I(4, 0, 4));
    Assert.Equal(65f, unit.EffectiveStat<AimStat>()); // rung 1 holds nothing

    combatant.Rank.Gain(100); // -> Squaddie
    var squaddie = battle.Spawn(combatant, new Vector3I(4, 0, 5));
    Assert.Equal(70f, squaddie.EffectiveStat<AimStat>());

    combatant.Rank.Gain(100); // -> Corporal: both rungs' mods stack
    var corporal = battle.Spawn(combatant, new Vector3I(4, 0, 6));
    Assert.Equal(80f, corporal.EffectiveStat<AimStat>());
    Assert.Equal(2, combatant.Rank.StatMods().Count);
  }

  [TestCase(TestName = "Held rung buffs fold into InnateBuffs and stack at spawn")]
  public void RungBuffsFoldIntoInnateBuffs()
  {
    var faction = TestData.MakeFaction("Player");
    using var battle = new BattleFixture(new Vector3I(8, 1, 8), [faction]);
    var condition = new HealthBelowPercentCondition { Percent = 50f };
    var promoted = TestData.MakeBuff("Veteran", condition);
    var table = TestData.MakeRankTable(
      new RankLevelData { Name = "Rookie", GainFactorPercent = 100 },
      new RankLevelData
      {
        Name = "Squaddie",
        GainFactorPercent = 100,
        Effects = [TestData.MakeBuffGrantEffect(promoted)],
      });
    var combatant = TestData.MakeCombatant("Alpha", faction,
      health: 10, actionPoints: 2, movement: 8, rankTable: table);

    Assert.Equal(0, combatant.InnateBuffs.Count); // nothing held yet

    combatant.Rank.Gain(100); // -> Squaddie
    Assert.Equal(1, combatant.InnateBuffs.Count); // live surface picks up the promotion

    var unit = battle.Spawn(combatant, new Vector3I(4, 0, 4));
    Assert.Equal(1, unit.Buffs.Count);
  }

  [TestCase(TestName = "Held rung abilities surface, and a null ability names its rung")]
  public void RungAbilitiesSurface()
  {
    var faction = TestData.MakeFaction("Player");
    var combatant = TestData.MakeCombatant("Alpha", faction, health: 10, actionPoints: 2, movement: 8,
      rankTable: TestData.MakeRankTable(
        new RankLevelData { Name = "Rookie", GainFactorPercent = 100 },
        new RankLevelData
        {
          Name = "Sergeant",
          GainFactorPercent = 100,
          Effects = [TestData.MakeAbilityGrantEffect("FieldCommand")],
        }));
    Assert.Equal(0, combatant.Rank.GrantedAbilities().Count);

    combatant.Rank.Gain(100);
    Assert.Equal(1, combatant.Rank.GrantedAbilities().Count);
    Assert.Equal("FieldCommand", combatant.Rank.GrantedAbilities()[0].Name);

    var broken = TestData.MakeCombatant("Alpha", faction, health: 10, actionPoints: 2, movement: 8,
      rankTable: TestData.MakeRankTable(
        new RankLevelData { Name = "Rookie", GainFactorPercent = 100 },
        new RankLevelData
        {
          Name = "Sergeant",
          GainFactorPercent = 100,
          Effects = [new AbilityGrantUpgradeEffectData { Ability = null }], // no Ability assigned
        }));
    broken.Rank.Gain(100);
    Assert.Throws<System.InvalidOperationException>(() => broken.Rank.GrantedAbilities());
  }
}
