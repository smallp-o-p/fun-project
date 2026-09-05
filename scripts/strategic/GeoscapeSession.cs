using CampaignGameState = global::FunProject.GameState.GameState;
using System;
using System.Collections.Generic;
using FunProject.Engineering;
using FunProject.GameState;
using FunProject.Items;

namespace FunProject.Strategic;

/// <summary>
/// The ephemeral geoscape runtime constructed over the campaign truth (<see cref="GameState"/>):
/// playback speed, the real-seconds accumulator, event dispatch, and resolution orchestration.
/// Mutates the state it wraps — the BattleSession-over-BattleBoardState pattern. One session
/// per campaign; rebuildable at any time (loading a save later reconstructs state, then session).
/// </summary>
public sealed class GeoscapeSession
{
  // One tick advances the in-game clock by one minute.
  public const int TickGameSeconds = CampaignGameState.TickGameSeconds;

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

  private readonly CampaignGameState _state;
  private double _accumulator;

  public GeoscapeSession(CampaignGameState state)
  {
    ArgumentNullException.ThrowIfNull(state);
    _state = state;
  }

  public event Action<IGeoscapeEvent> EventCommitted = delegate { };

  public long Tick => _state.Tick;

  public TimeSpeed Speed { get; private set; } = TimeSpeed.Paused;

  public DateTime CurrentTime => _state.CurrentTime;

  public int CurrentDay => _state.CurrentDay;

  public IReadOnlyList<RegionData> Regions => _state.Regions;

  public int IndexOfRegion(string name) => _state.IndexOfRegion(name);

  public IReadOnlyList<GeoscapeEvent> ActiveEvents => _state.ActiveEvents;

  public Option<PendingResolution> PendingResolution => _state.Pending;

  public Option<ManufacturingJob> ActiveManufacturing => _state.Engineering.ActiveJob;

  public IReadOnlyList<ManufacturingOption> GetManufacturingOptions()
    => _state.Engineering.GetManufacturingOptions(_state.Armory);

  public Either<ManufacturingStartFailure, ManufacturingJob> StartManufacturing(EquippableItemData data)
    => _state.Engineering.ResolveManufacturing(data, _state.Armory).Map(project =>
    {
      var job = new ManufacturingJob(project, Tick, CompletionTick(project.DurationDays));
      ManufacturingStarted started = _state.Engineering.Start(job);
      Commit(started);
      return started.Job;
    });

  private long CompletionTick(uint durationDays)
  {
    long seconds = durationDays * (TimeSpan.TicksPerDay / TimeSpan.TicksPerSecond);
    long ticks = (seconds + TickGameSeconds - 1) / TickGameSeconds;
    return checked(Tick + ticks);
  }

  public void OpenResolution(GeoscapeEvent @event)
  {
    ArgumentNullException.ThrowIfNull(@event);
    if (_state.Pending.IsSome)
      throw new InvalidOperationException("A resolution is already pending; complete it before opening another.");

    var pending = new PendingResolution(@event);
    _state.Pending = pending;

    Commit(new ResolutionEventOpened(pending));
  }

  public void CompleteResolution(ResolutionOutcome outcome)
  {
    if (_state.Pending.IsNone)
      throw new InvalidOperationException("CompleteResolution called with no resolution pending.");

    // Clear before committing: synchronous subscribers (HUD/map refreshes) must observe the
    // resolution as already closed — the battle layer's "hooks act on post-state" convention.
    _state.Pending.IfSome(pending =>
    {
      _state.Pending = Option<PendingResolution>.None;
      _state.ActiveEvents.Remove(pending.Event);
      Commit(new ResolutionEventClosed(pending, outcome));
    });
  }

  public void ChangeSpeed(TimeSpeed speed)
  {
    Speed = speed;
  }

  public void Advance(double deltaSeconds)
  {
    if (Speed == TimeSpeed.Paused || _state.Pending.IsSome)
      return;

    _accumulator += deltaSeconds;
    double secondsPerTick = SecondsPerTick(Speed);
    int ticksToAdvance = (int)(_accumulator / secondsPerTick);

    _accumulator -= ticksToAdvance * secondsPerTick;

    for (int i = 0; i < ticksToAdvance; i++)
    {
      _state.Tick++;
      Commit(new TimeAdvanced(Tick, CurrentTime));
      FireDueSchedule();
      RemoveExpired();
      _state.Engineering.CompleteIfDue(Tick).IfSome(Handle);
    }
  }

  private void Handle(ManufacturingCompleted completed)
  {
    var project = completed.Job.Project;
    _state.Armory.AddItem(project.Item, project.UnlimitedStock);
    Commit(completed);
  }

  private void Commit(IGeoscapeEvent geoscapeEvent)
  {
    EventCommitted.Invoke(geoscapeEvent);
  }

  private void FireDueSchedule()
  {
    while (_state.NextScheduleIndex < _state.Timeline.Count
           && _state.Timeline[_state.NextScheduleIndex].AtTick <= Tick)
    {
      CampaignGameState.ScheduledFire fire = _state.Timeline[_state.NextScheduleIndex];
      _state.NextScheduleIndex++;

      var active = new GeoscapeEvent(
        fire.Definition,
        fire.TargetRegionIndex >= 0 ? Some(fire.TargetRegionIndex) : Option<int>.None,
        Tick,
        fire.ExpiresAtTick);
      _state.ActiveEvents.Add(active);
      Commit(new ScheduledEventFired(active));
    }
  }

  private void RemoveExpired()
  {
    for (int i = _state.ActiveEvents.Count - 1; i >= 0; i--)
    {
      GeoscapeEvent active = _state.ActiveEvents[i];
      active.ExpiresAtTick.IfSome(expiry =>
      {
        if (expiry > Tick) return;
        _state.ActiveEvents.RemoveAt(i);
        Commit(new EventExpired(active));
      });
    }
  }
}
