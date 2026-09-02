using System;
using System.Collections.Generic;
using FunProject.Combatants;
using FunProject.Strategic;

namespace FunProject.GameState;

/// <summary>
/// The campaign truth container: everything that holds for the life of the campaign —
/// clock position, the baked event timeline, active events, the pending resolution, the
/// region binding, the roster, and the player faction. <see cref="Strategic.GeoscapeSession"/>
/// is the ephemeral runtime constructed over this state (the BattleSession-over-
/// BattleBoardState pattern); playback knobs (TimeSpeed, the frame accumulator) stay out
/// of the truth layer, mirroring X2's split where the strategy world is persisted whole
/// but time speed is not persisted at all.
/// </summary>
public sealed class GameState
{
  // One tick advances the in-game clock by one minute (mirrors GeoscapeSession.TickGameSeconds).
  public const int TickGameSeconds = 60;

  // A timeline entry baked at construction: definition, fire tick, resolved target (−1 =
  // map-wide), and absolute expiry tick. Authored resources stay mutable, so nothing is
  // reread after the bake.
  internal sealed record ScheduledFire(
    GeoscapeEventDefinition Definition,
    int AtTick,
    int TargetRegionIndex,
    Option<long> ExpiresAtTick);

  private readonly List<RegionData> _regions = [];
  private readonly Dictionary<string, int> _indexByName = [];
  private readonly List<Combatant> _roster = [];
  private readonly DateTime _startTime = DateTime.UnixEpoch;

  public GameState(CampaignStartData start)
  {
    ArgumentNullException.ThrowIfNull(start);

    GeoscapeMapData map = start.Map ?? throw new InvalidOperationException(
      "CampaignStartData requires a Map; assign one in the inspector.");
    FactionData playerFaction = start.PlayerFaction ?? throw new InvalidOperationException(
      "CampaignStartData requires a PlayerFaction; assign one in the inspector.");

    foreach (RegionData region in map.Regions)
    {
      if (!_indexByName.TryAdd(region.Name, _regions.Count))
        throw new InvalidOperationException(
          $"Duplicate region name '{region.Name}' in GeoscapeMapData.Regions; names must be unique.");

      _regions.Add(region);
    }

    Timeline = [.. map.Timeline.AsValueEnumerable()
      .Select(BakeEntry)
      .OrderBy(fire => fire.AtTick)];

    PlayerFaction = new Faction(playerFaction);

    foreach (RosterEntryData entry in start.StartingRoster)
    {
      CombatantData unit = entry.Unit ?? throw new InvalidOperationException(
        $"Starting roster entry '{entry.DisplayName}' has no Unit assigned in CampaignStartData.StartingRoster.");

      _roster.Add(new Combatant(unit, PlayerFaction,
        entry.DisplayName.Length > 0 ? Some(entry.DisplayName) : Option<string>.None));
    }

    Armory = new Armory(start.Armory, start.ModStock);
  }

  public IReadOnlyList<RegionData> Regions => _regions;

  // Index of the region with this name, or -1. The authoritative name-to-index binding,
  // produced once at construction from the Regions array.
  public int IndexOfRegion(string name) => _indexByName.GetValueOrDefault(name, -1);

  public IReadOnlyList<Combatant> Roster => _roster;

  public Faction PlayerFaction { get; }

  public Armory Armory { get; }

  public DateTime CurrentTime => _startTime + TimeSpan.FromSeconds(Tick * TickGameSeconds);

  // Days of game time elapsed since the start, 1-based: Day 1 spans the first 24 game-hours,
  // so an 08:00 start rolls to Day 2 at the next 08:00 — not at midnight.
  public int CurrentDay => (int)((CurrentTime - _startTime).TotalDays) + 1;

  // --- Session mutation surface (same assembly; GeoscapeSession is the designated mutator) ---

  internal long Tick { get; set; }

  internal List<ScheduledFire> Timeline { get; }

  internal int NextScheduleIndex { get; set; }

  internal List<GeoscapeEvent> ActiveEvents { get; } = [];

  internal Option<PendingResolution> Pending { get; set; }

  // Bakes one authored entry into an immutable firing. Unset events and out-of-range expiry
  // are authoring mistakes: they fail the load instead of being silently dropped.
  private ScheduledFire BakeEntry(ScheduledEventData entry)
  {
    if (entry.Event is null)
      throw new InvalidOperationException(
        $"Timeline entry at tick {entry.AtTick} has no Event assigned in GeoscapeMapData.Timeline.");

    if (entry.Event.ExpiresAfterTicks < -1)
      throw new InvalidOperationException(
        $"Event '{entry.Event.Title}' has ExpiresAfterTicks {entry.Event.ExpiresAfterTicks}; -1 means never, otherwise a non-negative tick count is required.");

    return new ScheduledFire(
      entry.Event,
      entry.AtTick,
      ResolveTargetIndex(entry.Event),
      entry.Event.ExpiresAfterTicks >= 0
        ? Some((long)entry.AtTick + entry.Event.ExpiresAfterTicks)
        : Option<long>.None);
  }

  // Name-to-index resolution for event targets; throws at construction on unknown names so
  // authoring mistakes fail at load, not at fire time.
  private int ResolveTargetIndex(GeoscapeEventDefinition definition)
  {
    if (definition.TargetRegionName.Length == 0)
      return -1;

    if (_indexByName.TryGetValue(definition.TargetRegionName, out int index))
      return index;

    throw new InvalidOperationException(
      $"Event '{definition.Title}' targets region '{definition.TargetRegionName}' which is not part of the map's Regions.");
  }
}
