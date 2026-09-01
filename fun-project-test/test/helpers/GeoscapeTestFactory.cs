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
}
