using Godot;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace FunProject.Strategic;

public sealed class GeoscapeSession
{
  // One tick advances the in-game clock by one minute.
  public const int TickGameSeconds = 60;

  private const double BaseSecondsPerTick = 0.1;
  private static double SecondsPerTick(TimeSpeed speed) => speed switch
  {
    TimeSpeed.Paused => 0.0,
    TimeSpeed.Normal => BaseSecondsPerTick,
    TimeSpeed.Fast => BaseSecondsPerTick / 5.0,
    TimeSpeed.VeryFast => BaseSecondsPerTick / 12.5,
    TimeSpeed.VeryVeryFast => BaseSecondsPerTick / 25.0,
    _ => throw new InvalidOperationException($"Unknown time speed '{speed}'."),
  };

  private readonly List<RegionData> _regions = [];
  private readonly Dictionary<string, int> _indexByName = [];
  private readonly List<ScheduledFire> _timeline;
  private readonly List<GeoscapeEvent> _activeEvents = [];
  private int _nextScheduleIndex;
  private double _accumulator;
  private Option<PendingResolution> _pending = None;

  // A timeline entry baked at construction: definition, fire tick, resolved target (−1 =
  // map-wide), and absolute expiry tick. Authored resources stay mutable, so nothing is
  // reread after the bake.
  private sealed record ScheduledFire(
    GeoscapeEventDefinition Definition,
    int AtTick,
    int TargetRegionIndex,
    Option<long> ExpiresAtTick);

  public GeoscapeSession(GeoscapeMapData mapData)
  {
    ArgumentNullException.ThrowIfNull(mapData);

    MapSize = mapData.Size;

    foreach (RegionData region in mapData.Regions)
    {
      if (!_indexByName.TryAdd(region.Name, _regions.Count))
        throw new InvalidOperationException(
          $"Duplicate region name '{region.Name}' in GeoscapeMapData.Regions; names must be unique.");

      _regions.Add(region);
    }

    _timeline = [.. mapData.Timeline.AsValueEnumerable()
      .Select(BakeEntry)
      .OrderBy(fire => fire.AtTick)];
  }

  public event Action<IGeoscapeEvent> EventCommitted = delegate { };

  public long Tick { get; private set; }

  public Vector2I MapSize { get; }

  public TimeSpeed Speed { get; private set; } = TimeSpeed.Paused;

  private readonly DateTime _startTime = DateTime.UnixEpoch;
  
  public DateTime CurrentTime => _startTime + TimeSpan.FromSeconds(Tick * TickGameSeconds);

  // Days of game time elapsed since the start, 1-based: Day 1 spans the first 24 game-hours,
  // so an 08:00 start rolls to Day 2 at the next 08:00 — not at midnight.
  public int CurrentDay => (int)((CurrentTime - _startTime).TotalDays) + 1;

  public IReadOnlyList<RegionData> Regions => _regions;

  // Index of the region with this name, or -1. The authoritative name-to-index binding,
  // produced once at construction from the Regions array.
  public int IndexOfRegion(string name) => _indexByName.GetValueOrDefault(name, -1);

  public IReadOnlyList<GeoscapeEvent> ActiveEvents => _activeEvents;

  public Option<PendingResolution> PendingResolution => _pending;

  public void OpenResolution(GeoscapeEvent @event)
  {
    ArgumentNullException.ThrowIfNull(@event);
    if (_pending.IsSome)
      throw new InvalidOperationException("A resolution is already pending; complete it before opening another.");

    var pending = new PendingResolution(@event);
    _pending = pending;

    Commit(new ResolutionEventOpened(pending));
  }

  public void CompleteResolution(ResolutionOutcome outcome)
  {
    if (_pending.IsNone)
      throw new InvalidOperationException("CompleteResolution called with no resolution pending.");

    // Clear before committing: synchronous subscribers (HUD/map refreshes) must observe the
    // resolution as already closed — the battle layer's "hooks act on post-state" convention.
    var resolved = _pending;
    _pending = None;
    resolved.IfSome(pending =>
    {
      _activeEvents.Remove(pending.Event);
      Commit(new ResolutionEventClosed(pending, outcome));
    });
  }

  public void ChangeSpeed(TimeSpeed speed)
  {
    Speed = speed;
  }

  public void Advance(double deltaSeconds)
  {
    if (Speed == TimeSpeed.Paused || _pending.IsSome)
      return;

    _accumulator += deltaSeconds;
    double secondsPerTick = SecondsPerTick(Speed);
    int ticksToAdvance = (int)(_accumulator / secondsPerTick);

    _accumulator -= ticksToAdvance * secondsPerTick;

    for (int i = 0; i < ticksToAdvance; i++)
    {
      Tick++;
      Commit(new TimeAdvanced(Tick, CurrentTime));
      FireDueSchedule();
      RemoveExpired();
    }
  }

  private void Commit(IGeoscapeEvent geoscapeEvent)
  {
    EventCommitted.Invoke(geoscapeEvent);
  }

  private void FireDueSchedule()
  {
    while (_nextScheduleIndex < _timeline.Count && _timeline[_nextScheduleIndex].AtTick <= Tick)
    {
      ScheduledFire fire = _timeline[_nextScheduleIndex];
      _nextScheduleIndex++;

      var active = new GeoscapeEvent(
        fire.Definition,
        fire.TargetRegionIndex >= 0 ? Some(fire.TargetRegionIndex) : Option<int>.None,
        Tick,
        fire.ExpiresAtTick);
      _activeEvents.Add(active);
      Commit(new ScheduledEventFired(active));
    }
  }

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

  private void RemoveExpired()
  {
    for (int i = _activeEvents.Count - 1; i >= 0; i--)
    {
      GeoscapeEvent active = _activeEvents[i];
      active.ExpiresAtTick.IfSome(expiry =>
      {
        if (expiry > Tick) return;
        _activeEvents.RemoveAt(i);
        Commit(new EventExpired(active));
      });
    }
  }
}
