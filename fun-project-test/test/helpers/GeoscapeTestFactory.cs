using FunProject.Combatants;
using FunProject.GameState;
using FunProject.Stats;
using FunProject.Strategic;

namespace FunProject.Tests;

internal static class GeoscapeTestFactory
{
  public static RegionData MakeRegion(string name)
  {
    return new RegionData
    {
      Name = name,
      FlavorText = $"{name} flavor text.",
    };
  }

  public static GeoscapeSession MakeSession(
    RegionData[]? regions = null,
    ScheduledEventData[]? timeline = null)
  {
    return new GeoscapeSession(new GeoscapeMapData
    {
      Regions = regions ?? [],
      Timeline = timeline ?? [],
    });
  }

  public static GeoscapeEventDefinition MakeEvent(
    string title,
    GeoscapeEventKind kind = GeoscapeEventKind.Plot,
    string targetRegionName = "",
    int expiresAfterTicks = -1)
  {
    return new GeoscapeEventDefinition
    {
      Kind = kind,
      Title = title,
      Description = $"{title} description.",
      ExpiresAfterTicks = expiresAfterTicks,
      TargetRegionName = targetRegionName,
    };
  }

  public static ScheduledEventData MakeScheduled(int atTick, GeoscapeEventDefinition evt)
  {
    return new ScheduledEventData { AtTick = atTick, Event = evt };
  }

  public static CombatantData MakeCombatantData(string name = "Mold")
  {
    return new CombatantData
    {
      Name = name,
      HealthStat = new HealthStat(),
      ActionPointsStat = new ActionPointsStat(),
      WillStat = new WillStat(),
      MovementStat = new MovementStat(),
      VisionStat = new VisionStat(),
      AimStat = new AimStat(),
    };
  }

  public static RosterEntryData MakeEntry(string displayName = "", CombatantData? unit = null)
  {
    return new RosterEntryData
    {
      Unit = unit ?? MakeCombatantData(),
      DisplayName = displayName,
    };
  }

  public static CampaignStartData MakeStart(
    RegionData[]? regions = null,
    ScheduledEventData[]? timeline = null,
    RosterEntryData[]? roster = null)
  {
    return new CampaignStartData
    {
      Name = "Test Campaign",
      Map = new GeoscapeMapData { Regions = regions ?? [], Timeline = timeline ?? [] },
      PlayerFaction = new FactionData { Name = "Test Faction" },
      StartingRoster = roster ?? [],
    };
  }
}
