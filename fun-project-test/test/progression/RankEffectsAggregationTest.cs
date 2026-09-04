using FunProject.Buffs;
using FunProject.Combatants;
using FunProject.Progression;
using FunProject.Stats;
using GdUnit4;
using Godot;
using static FunProject.Tests.ProgressionTestFactory;

[TestSuite]
[RequireGodotRuntime]
public partial class RankEffectsAggregationTest
{
  private static RankTableData MakeTable(params (string Name, int Factor, UpgradeEffectData[] Effects)[] levels)
  {
    var table = new RankTableData();
    foreach ((string name, int factor, UpgradeEffectData[] effects) in levels)
    {
      var level = new RankLevelData { Name = name, GainFactorPercent = factor };
      foreach (UpgradeEffectData effect in effects)
        level.Effects.Add(effect);
      table.Levels.Add(level);
    }
    return table;
  }

  private static Combatant MakeRanked(Faction faction, RankTableData table, string name = "Alpha")
  {
    var data = new CombatantData
    {
      Name = name,
      HealthStat = new HealthStat { BaseValue = 10 },
      ActionPointsStat = new ActionPointsStat { BaseValue = 2 },
      WillStat = new WillStat { BaseValue = 50 },
      MovementStat = new MovementStat { BaseValue = 8 },
      VisionStat = new VisionStat { BaseValue = 20 },
      AimStat = new AimStat { BaseValue = 65 },
      RankTable = table,
    };
    return new Combatant(data, faction);
  }

  [TestCase(TestName = "A missing rank table throws loudly at combatant construction")]
  public void MissingTableThrows()
  {
    // No authored RankTable and the test runtime has no res://resources/ranks.tres — the
    // default load must fail loudly, never degrade silently.
    Assert.Throws<System.InvalidOperationException>(()
      => new Combatant(new CombatantData
      {
        Name = "Alpha",
        HealthStat = new HealthStat { BaseValue = 10 },
        ActionPointsStat = new ActionPointsStat { BaseValue = 2 },
        WillStat = new WillStat { BaseValue = 50 },
        MovementStat = new MovementStat { BaseValue = 8 },
        VisionStat = new VisionStat { BaseValue = 20 },
        AimStat = new AimStat { BaseValue = 65 },
      }, MakeFaction("Player")));
  }

  [TestCase(TestName = "Held rung stat mods are cumulative and raise the spawned unit's effective aim")]
  public void RungStatModsAreCumulative()
  {
    var faction = MakeFaction("Player");
    var session = MakeSession(new Vector3I(8, 1, 8), [faction]);
    var combatant = MakeRanked(faction, MakeTable(
      ("Rookie", 100, []),
      ("Squaddie", 100, [MakeStatModEffect(new AimStatMod { Modifiers = [StatModifier.Add(5)] })]),
      ("Corporal", 100, [MakeStatModEffect(new AimStatMod { Modifiers = [StatModifier.Add(10)] })])));

    var unit = SpawnUnit(session, combatant, new Vector3I(4, 0, 4));
    Assert.Equal(65f, unit.State.EffectiveStat<AimStat>()); // rung 1 holds nothing

    combatant.Rank.Gain(100); // -> Squaddie
    var squaddie = SpawnUnit(session, combatant, new Vector3I(4, 0, 5));
    Assert.Equal(70f, squaddie.State.EffectiveStat<AimStat>());

    combatant.Rank.Gain(100); // -> Corporal: both rungs' mods stack
    var corporal = SpawnUnit(session, combatant, new Vector3I(4, 0, 6));
    Assert.Equal(80f, corporal.State.EffectiveStat<AimStat>());
    Assert.Equal(2, combatant.Rank.StatMods().Count);
  }

  [TestCase(TestName = "Held rung buffs fold into InnateBuffs and stack at spawn")]
  public void RungBuffsFoldIntoInnateBuffs()
  {
    var faction = MakeFaction("Player");
    var session = MakeSession(new Vector3I(8, 1, 8), [faction]);
    var condition = new HealthBelowPercentConditionData { Percent = 50f };
    var promoted = MakeBuff("Veteran", condition);
    var combatant = MakeRanked(faction, MakeTable(
      ("Rookie", 100, []),
      ("Squaddie", 100, [MakeBuffGrantEffect(promoted)])));

    Assert.Equal(0, combatant.InnateBuffs.Count); // nothing held yet

    combatant.Rank.Gain(100); // -> Squaddie
    Assert.Equal(1, combatant.InnateBuffs.Count); // live surface picks up the promotion

    var unit = SpawnUnit(session, combatant, new Vector3I(4, 0, 4));
    Assert.Equal(1, unit.State.Buffs.Count);
  }

  [TestCase(TestName = "Held rung abilities surface, and a null ability names its rung")]
  public void RungAbilitiesSurface()
  {
    var faction = MakeFaction("Player");
    var combatant = MakeRanked(faction, MakeTable(
      ("Rookie", 100, []),
      ("Sergeant", 100, [MakeAbilityGrantEffect("FieldCommand")])));
    Assert.Equal(0, combatant.Rank.GrantedAbilities().Count);

    combatant.Rank.Gain(100);
    Assert.Equal(1, combatant.Rank.GrantedAbilities().Count);
    Assert.Equal("FieldCommand", combatant.Rank.GrantedAbilities()[0].Name);

    var broken = MakeRanked(faction, MakeTable(
      ("Rookie", 100, []),
      ("Sergeant", 100, [new AbilityGrantUpgradeEffectData { Ability = null }]))); // no Ability assigned
    broken.Rank.Gain(100);
    Assert.Throws<System.InvalidOperationException>(() => broken.Rank.GrantedAbilities());
  }
}
