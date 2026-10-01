using FunProject.Battle;
using FunProject.Strategic;
using GdUnit4;
using System;
using System.Collections.Generic;

[TestSuite]
[RequireGodotRuntime]
public class MissionEnemyResolverTest
{
  [TestCase(TestName = "Special enemies are additive: ordinary draws first, mandatory specials in the final positions")]
  public void SpecialEnemiesAreAdditive()
  {
    var mission = TestData.MakeTacticalMission(minEnemyUnits: 2, maxEnemyUnits: 2);
    var specialOne = new UnitLoadoutData { Combatant = TestData.MakeCombatantData("Special One", health: 30) };
    var specialTwo = new UnitLoadoutData { Combatant = TestData.MakeCombatantData("Special Two", health: 30) };
    mission.SpecialEnemies.Add(specialOne);
    mission.SpecialEnemies.Add(specialTwo);

    SideDeployment force = MissionEnemyResolver.Resolve(mission, seed: 7);

    Assert.Equal(1, force.FactionIndex);
    Assert.Equal(4, force.Loadouts.Count);
    Assert.Equal("Grunt", force.Loadouts[0].Combatant.Name);
    Assert.Equal("Grunt", force.Loadouts[1].Combatant.Name);
    Assert.Equal(specialOne.Combatant.Name, force.Loadouts[2].Combatant.Name);
    Assert.Equal(specialTwo.Combatant.Name, force.Loadouts[3].Combatant.Name);
    foreach (UnitLoadout loadout in force.Loadouts)
      Assert.True(ReferenceEquals(loadout.Combatant.OwningFaction, force.Faction));
  }

  [TestCase(TestName = "A specials-only mission needs no ordinary pool and produces exactly the specials")]
  public void SpecialOnlyMissionNeedsNoOrdinaryPool()
  {
    var mission = TestData.MakeTacticalMission(minEnemyUnits: 0, maxEnemyUnits: 0);
    mission.SpecialEnemies.Add(new UnitLoadoutData
    {
      Combatant = TestData.MakeCombatantData("Warlock", health: 30),
    });

    SideDeployment force = MissionEnemyResolver.Resolve(mission, seed: 7);

    Assert.Equal(1, force.Loadouts.Count);
    Assert.Equal("Warlock", force.Loadouts[0].Combatant.Name);
  }

  [TestCase(TestName = "Resolution mints a fresh faction and separate instances for every unit")]
  public void ResolutionMintsFreshInstances()
  {
    var mission = TestData.MakeTacticalMission(minEnemyUnits: 2, maxEnemyUnits: 2);

    SideDeployment first = MissionEnemyResolver.Resolve(mission, seed: 7);
    SideDeployment second = MissionEnemyResolver.Resolve(mission, seed: 7);

    Assert.False(ReferenceEquals(first.Faction, second.Faction));
    Assert.Equal(first.Faction.Name, second.Faction.Name);
    for (int index = 0; index < first.Loadouts.Count; index++)
    {
      Assert.False(ReferenceEquals(first.Loadouts[index].Combatant, second.Loadouts[index].Combatant));
      Assert.False(ReferenceEquals(
        first.Loadouts[index].Combatant.OwningFaction,
        second.Loadouts[index].Combatant.OwningFaction));
    }
  }

  [TestCase(TestName = "Enemy count sampling observes both endpoints of an extreme inclusive range")]
  public void ExtremeRangeSamplingObservesBothEndpoints()
  {
    var size = new BattleSizeData
    {
      MaxPlayerUnits = 1,
      MinEnemyUnits = int.MaxValue - 1,
      MaxEnemyUnits = int.MaxValue,
    };
    int minimumDraws = 0;
    int maximumDraws = 0;

    foreach (int seed in new[] { 1, 2, 3, 4, 5, 6, 7, 8 })
    {
      int count = size.SampleEnemyCount(new Random(seed));
      if (count == int.MaxValue - 1)
        minimumDraws++;
      if (count == int.MaxValue)
        maximumDraws++;
    }

    Assert.True(minimumDraws > 0, "Never sampled the minimum endpoint int.MaxValue - 1.");
    Assert.True(maximumDraws > 0, "Never sampled the inclusive maximum endpoint int.MaxValue.");
  }

  [TestCase(TestName = "Enemy count sampling respects small, zero, and fixed inclusive bounds")]
  public void BoundedRangeSamplingStaysInclusive()
  {
    var small = new BattleSizeData { MaxPlayerUnits = 1, MinEnemyUnits = 2, MaxEnemyUnits = 5 };
    var zero = new BattleSizeData { MaxPlayerUnits = 3, MinEnemyUnits = 0, MaxEnemyUnits = 0 };
    var fixedRange = new BattleSizeData { MaxPlayerUnits = 3, MinEnemyUnits = 4, MaxEnemyUnits = 4 };

    foreach (int seed in new[] { 7, -7, 99 })
    {
      int drawn = small.SampleEnemyCount(new Random(seed));
      Assert.True(drawn >= 2 && drawn <= 5, $"Seed {seed} drew {drawn} outside [2, 5].");
      Assert.Equal(0, zero.SampleEnemyCount(new Random(seed)));
      Assert.Equal(4, fixedRange.SampleEnemyCount(new Random(seed)));
    }
  }

  [TestCase(TestName = "Malformed mission authoring fails validation")]
  public void MalformedMissionAuthoringFailsValidation()
  {
    var missingEntryCombatant = TestData.MakeTacticalMission();
    missingEntryCombatant.OrdinaryEnemies[0].Combatant = null!;
    var playerSlot = TestData.MakeTacticalMission();
    playerSlot.EnemyFactionIndex = 0;
    var outOfRangeSlot = TestData.MakeTacticalMission();
    outOfRangeSlot.EnemyFactionIndex = 2;
    var brokenPlayerIndex = TestData.MakeTacticalMission();
    brokenPlayerIndex.BattleType.PlayerFactionIndex = -1;
    var nullEnemySlot = TestData.MakeTacticalMission();
    nullEnemySlot.BattleType.Factions[1] = null!;
    var nullPlayerFaction = TestData.MakeTacticalMission();
    nullPlayerFaction.BattleType.Factions[0].Faction = null!;
    var nullEnemyFaction = TestData.MakeTacticalMission();
    nullEnemyFaction.BattleType.Factions[1].Faction = null!;

    (string Case, Func<TacticalMissionData> Build)[] malformed =
    [
      ("negative minimum", () => TestData.MakeTacticalMission(minEnemyUnits: -1)),
      ("minimum above maximum", () => TestData.MakeTacticalMission(minEnemyUnits: 2, maxEnemyUnits: 1)),
      ("non-positive player capacity", () => TestData.MakeTacticalMission(maxPlayerUnits: 0)),
      ("missing battle type", () => new TacticalMissionData
      {
        BattleType = null!,
        Size = new BattleSizeData { MaxPlayerUnits = 3, MinEnemyUnits = 1, MaxEnemyUnits = 1 },
        EnemyFactionIndex = 1,
      }),
      ("missing size", () => new TacticalMissionData
      {
        BattleType = TestData.MakeDuelBattleType(),
        Size = null!,
        EnemyFactionIndex = 1,
      }),
      ("loadout entry without a combatant", () => missingEntryCombatant),
      ("enemy slot on the player faction", () => playerSlot),
      ("enemy slot out of range", () => outOfRangeSlot),
      ("battle type with an out-of-range player index", () => brokenPlayerIndex),
      ("enemy slot without a deployment", () => nullEnemySlot),
      ("player slot without a faction", () => nullPlayerFaction),
      ("enemy slot without a faction", () => nullEnemyFaction),
      ("empty ordinary pool with a positive maximum", () => new TacticalMissionData
      {
        BattleType = TestData.MakeDuelBattleType(),
        Size = new BattleSizeData { MaxPlayerUnits = 3, MinEnemyUnits = 1, MaxEnemyUnits = 1 },
        EnemyFactionIndex = 1,
      }),
    ];
    foreach ((string name, Func<TacticalMissionData> build) in malformed)
      Assert.Throws<InvalidOperationException>(() => build().Validate(), name);
  }
}
