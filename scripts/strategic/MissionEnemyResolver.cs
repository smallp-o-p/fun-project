using FunProject.Battle;
using FunProject.Combatants;
using System;
using System.Collections.Generic;

namespace FunProject.Strategic;

/// <summary>Deterministic enemy-force generation for tactical missions: one fresh faction
/// per resolution, ordinary enemies drawn by pool position with replacement, mandatory
/// specials appended. The stream derives from the event's retained battle seed, so the same
/// seed reproduces the same force across session rebuilds.</summary>
public static class MissionEnemyResolver
{
  // Keeps enemy draws on a stream position distinct from seeded map selection.
  private const int StreamSalt = unchecked((int)0x6A09E667);

  public static SideDeployment Resolve(TacticalMissionData mission, int seed)
  {
    ArgumentNullException.ThrowIfNull(mission);
    mission.Validate();

    var random = new Random(unchecked(seed ^ StreamSalt));
    int ordinaryCount = mission.Size.SampleEnemyCount(random);

    FactionDeploymentData authoredSlot = mission.BattleType.Factions[mission.EnemyFactionIndex];
    var faction = new Faction(authoredSlot.Faction);
    var loadouts = new List<UnitLoadout>(ordinaryCount + mission.SpecialEnemies.Count);
    for (int index = 0; index < ordinaryCount; index++)
      loadouts.Add(mission.OrdinaryEnemies[random.Next(mission.OrdinaryEnemies.Count)].CreateRuntime(faction));
    foreach (UnitLoadoutData special in mission.SpecialEnemies)
      loadouts.Add(special.CreateRuntime(faction));

    return new SideDeployment(mission.EnemyFactionIndex, faction, loadouts);
  }
}
