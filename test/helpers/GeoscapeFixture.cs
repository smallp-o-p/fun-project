#nullable disable warnings
using System;
using System.Collections.Generic;
using CampaignGameState = global::FunProject.GameState.GameState;
using FunProject.GameState;
using FunProject.Strategic;
using ZLinq;

namespace FunProject.Tests;

// The owning test fixture for geoscape tests: one retained CampaignGameState/GeoscapeSession
// pair per fixture instance, with the fixture recording every committed event behind an
// explicit assertion window (ClearEvents starts a new one; nothing clears it implicitly).
// Neither state nor session has a Dispose API, so disposal only detaches the fixture's own
// EventCommitted subscription and closes the fixture; the retained pair stays inspectable
// afterwards for lifecycle tests. AdvanceTicks is a setup convenience mirroring the old
// SessionAt/fired-event helpers: switch to Normal, advance once by ticks * 0.1, stay Normal.
// Raw speed/accumulator scenarios call ChangeSpeed/Advance with their own literal inputs.
public sealed class GeoscapeFixture : IDisposable
{
  private readonly List<IGeoscapeEvent> _events = [];
  private bool _disposed;

  public CampaignGameState State { get; }
  public GeoscapeSession Session { get; }
  public IReadOnlyList<IGeoscapeEvent> Events => _events;
  public GeoscapeEvent ActiveEvent => Session.ActiveEvents.AsValueEnumerable().Single();

  public GeoscapeFixture(CampaignStartData start)
  {
    State = new CampaignGameState(start);
    Session = new GeoscapeSession(State);
    Session.EventCommitted += RecordEvent;
  }

  public GeoscapeFixture(RegionData[] regions = null, ScheduledEventData[] timeline = null)
    : this(TestData.MakeStart(regions, timeline)) { }

  public static GeoscapeFixture WithFiredEvent(GeoscapeEventDefinition definition, RegionData[] regions = null)
  {
    var campaign = new GeoscapeFixture(regions, [TestData.MakeScheduled(1, definition)]);
    try
    {
      campaign.AdvanceTicks(1);
      return campaign;
    }
    catch
    {
      campaign.Dispose();
      throw;
    }
  }

  public void ChangeSpeed(TimeSpeed speed) { ThrowIfDisposed(); Session.ChangeSpeed(speed); }
  public void Advance(double deltaSeconds) { ThrowIfDisposed(); Session.Advance(deltaSeconds); }
  public void OpenResolution(GeoscapeEvent value) { ThrowIfDisposed(); Session.OpenResolution(value); }
  public void CompleteResolution(ResolutionOutcome outcome) { ThrowIfDisposed(); Session.CompleteResolution(outcome); }

  // Explicitly starts a new assertion window; never called by conveniences.
  public void ClearEvents() { ThrowIfDisposed(); _events.Clear(); }

  public void AdvanceTicks(int ticks)
  {
    ThrowIfDisposed();
    ArgumentOutOfRangeException.ThrowIfNegative(ticks);
    Session.ChangeSpeed(TimeSpeed.Normal);
    Session.Advance(ticks * 0.1);
  }

  public void Dispose()
  {
    if (_disposed)
      return;
    Session.EventCommitted -= RecordEvent;
    _disposed = true;
  }

  private void RecordEvent(IGeoscapeEvent value) => _events.Add(value);

  private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
