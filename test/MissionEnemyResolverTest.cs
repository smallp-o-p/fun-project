using FunProject.Battle;
using FunProject.Strategic;
using GdUnit4;
using System;
using System.Collections.Generic;

namespace FunProject.Tests;

[TestSuite]
[RequireGodotRuntime]
public class MissionEnemyResolverTest
{
  [TestCase(TestName = "Seeded forces repeat with fresh units and ordered additive specials")]
  public static void SeededForcesRepeatWithFreshUnitsAndAdditiveSpecials()
  {
    var mission = TestData.MakeTacticalMission(minEnemyUnits: 2, maxEnemyUnits: 4);
    mission.OrdinaryEnemies.Add(new UnitLoadoutData
    { Combatant = TestData.MakeCombatantData("Heavy", health: 25) });
    mission.SpecialEnemies.Add(new UnitLoadoutData
    { Combatant = TestData.MakeCombatantData("Special One", health: 30) });
    mission.SpecialEnemies.Add(new UnitLoadoutData
    { Combatant = TestData.MakeCombatantData("Special Two", health: 30) });
    var names = new SysColGeneric.HashSet<string>();
    var counts = new SysColGeneric.HashSet<int>();

    for (int seed = 1; seed <= 32; seed++)
    {
      SideDeployment first = MissionEnemyResolver.Resolve(mission, seed);
      SideDeployment second = MissionEnemyResolver.Resolve(mission, seed);
      int ordinaryCount = first.Loadouts.Count - 2;
      Assert.True(ordinaryCount >= 2 && ordinaryCount <= 4);
      counts.Add(ordinaryCount);
      Assert.Equal(1, first.FactionIndex);
      Assert.Equal("Special One", first.Loadouts[ordinaryCount].Combatant.Name);
      Assert.Equal("Special Two", first.Loadouts[ordinaryCount + 1].Combatant.Name);
      Assert.False(ReferenceEquals(first.Faction, second.Faction));
      Assert.Equal(first.Faction.Name, second.Faction.Name);
      Assert.Equal(first.Loadouts.Count, second.Loadouts.Count);
      for (int index = 0; index < first.Loadouts.Count; index++)
      {
        UnitLoadout loadout = first.Loadouts[index];
        if (index < ordinaryCount)
          names.Add(loadout.Combatant.Name);
        Assert.Equal(loadout.Combatant.Name, second.Loadouts[index].Combatant.Name);
        Assert.False(ReferenceEquals(loadout.Combatant, second.Loadouts[index].Combatant));
        Assert.True(ReferenceEquals(first.Faction, loadout.Combatant.OwningFaction));
        Assert.True(ReferenceEquals(second.Faction, second.Loadouts[index].Combatant.OwningFaction));
      }
    }
    Assert.True(names.Contains("Grunt") && names.Contains("Heavy"));
    Assert.Equal(2, names.Count);
    Assert.True(counts.Count >= 2);
  }

  [TestCase(TestName = "A specials-only mission needs no ordinary pool and produces exactly the specials")]
  public static void SpecialOnlyMissionNeedsNoOrdinaryPool()
  {
    var mission = TestData.MakeTacticalMission(minEnemyUnits: 0, maxEnemyUnits: 0);
    mission.OrdinaryEnemies.Clear();
    mission.SpecialEnemies.Add(new UnitLoadoutData
    {
      Combatant = TestData.MakeCombatantData("Warlock", health: 30),
    });

    SideDeployment force = MissionEnemyResolver.Resolve(mission, seed: 7);

    Assert.Equal(1, force.Loadouts.Count);
    Assert.Equal("Warlock", force.Loadouts[0].Combatant.Name);
  }

  [TestCase(TestName = "Enemy count sampling observes both endpoints of an extreme inclusive range")]
  public static void ExtremeRangeSamplingObservesBothEndpoints()
  {
    var size = new BattleSizeData
    {
      MaxPlayerUnits = 1,
      MinEnemyUnits = uint.MaxValue - 1,
      MaxEnemyUnits = uint.MaxValue,
    };
    int minimumDraws = 0;
    int maximumDraws = 0;

    foreach (int seed in new[] { 1, 2, 3, 4, 5, 6, 7, 8 })
    {
      uint count = size.SampleEnemyCount(new Random(seed));
      if (count == uint.MaxValue - 1)
        minimumDraws++;
      if (count == uint.MaxValue)
        maximumDraws++;
    }

    Assert.True(minimumDraws > 0, "Never sampled the minimum endpoint uint.MaxValue - 1.");
    Assert.True(maximumDraws > 0, "Never sampled the inclusive maximum endpoint uint.MaxValue.");
  }

  [TestCase(TestName = "Enemy count sampling respects small, zero, and fixed inclusive bounds")]
  public static void BoundedRangeSamplingStaysInclusive()
  {
    var small = new BattleSizeData { MaxPlayerUnits = 1, MinEnemyUnits = 2, MaxEnemyUnits = 5 };
    var zero = new BattleSizeData { MaxPlayerUnits = 3, MinEnemyUnits = 0, MaxEnemyUnits = 0 };
    var fixedRange = new BattleSizeData { MaxPlayerUnits = 3, MinEnemyUnits = 4, MaxEnemyUnits = 4 };

    foreach (int seed in new[] { 7, -7, 99 })
    {
      uint drawn = small.SampleEnemyCount(new Random(seed));
      Assert.True(drawn >= 2 && drawn <= 5, $"Seed {seed} drew {drawn} outside [2, 5].");
      Assert.Equal(0u, zero.SampleEnemyCount(new Random(seed)));
      Assert.Equal(4u, fixedRange.SampleEnemyCount(new Random(seed)));
    }
  }

  [TestCase(true)]
  [TestCase(false)]
  public static void EnemyMinimumClampsWithoutLosingAssignmentOrder(bool minimumFirst)
  {
    using var size = new BattleSizeData();
    if (minimumFirst)
    {
      size.MinEnemyUnits = 2;
      size.MaxEnemyUnits = 4;
    }
    else
    {
      size.MaxEnemyUnits = 4;
      size.MinEnemyUnits = 2;
    }
    Assert.Equal(2u, size.MinEnemyUnits);
    size.MinEnemyUnits = 8;
    Assert.Equal(4u, size.MinEnemyUnits);
    size.MaxEnemyUnits = 1;
    Assert.Equal(1u, size.MinEnemyUnits);
    size.MaxPlayerUnits = 0;
    Assert.Equal(1u, size.MaxPlayerUnits);
  }

  [TestCase(TestName = "Battle size exports round trip with the authored minimum and unsigned maximum")]
  public static void BattleSizeResourceRoundTrip()
  {
    using var size = new BattleSizeData
    {
      MinEnemyUnits = 2,
      MaxEnemyUnits = 4,
      MaxPlayerUnits = uint.MaxValue,
    };
    var minimumProperty = size.GetPropertyList().AsValueEnumerable()
      .Single(property => property["name"].AsString() == BattleSizeData.PropertyName.MinEnemyUnits);
    Assert.Equal("0,4,1", minimumProperty["hint_string"].AsString());
    const string path = "user://handoff-battle-size.tres";
    try
    {
      Assert.Equal(Godot.Error.Ok, Godot.ResourceSaver.Save(size, path));
      using var loaded = Godot.ResourceLoader.Load<BattleSizeData>(path, cacheMode: Godot.ResourceLoader.CacheMode.Ignore);
      Assert.Equal(2u, loaded.MinEnemyUnits);
      Assert.Equal(4u, loaded.MaxEnemyUnits);
      Assert.Equal(uint.MaxValue, loaded.MaxPlayerUnits);
    }
    finally
    {
      Godot.DirAccess.RemoveAbsolute(Godot.ProjectSettings.GlobalizePath(path));
    }
  }
}
