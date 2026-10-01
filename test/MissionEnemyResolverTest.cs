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

  [TestCase(TestName = "Malformed mission authoring fails validation")]
  public void MalformedMissionAuthoringFailsValidation()
  {
    var missingEntryCombatant = TestData.MakeTacticalMission();
    missingEntryCombatant.OrdinaryEnemies[0].Combatant = null!;
    var playerSlot = TestData.MakeTacticalMission();
    playerSlot.EnemyFactionIndex = 0;
    var outOfRangeSlot = TestData.MakeTacticalMission();
    outOfRangeSlot.EnemyFactionIndex = 2;

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
