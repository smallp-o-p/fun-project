using System;
using FunProject.Strategic;
using GdUnit4;

namespace FunProject.Tests;

[TestSuite]
[RequireGodotRuntime]
public partial class GeoscapeFixtureTest
{
  [TestCase]
  public void StateSessionAndRecordingSurviveRepeatedOperations()
  {
    var definition = TestData.MakeEvent("Incident");
    using var campaign = new GeoscapeFixture(timeline: [TestData.MakeScheduled(1, definition)]);
    Assert.Equal(TimeSpeed.Paused, campaign.Session.Speed);
    campaign.Advance(1.0);
    Assert.Equal(0L, campaign.Session.Tick);
    campaign.AdvanceTicks(1);
    Assert.Equal(1L, campaign.State.Tick);
    Assert.Equal(campaign.State.Tick, campaign.Session.Tick);
    Assert.True(ReferenceEquals(definition, campaign.ActiveEvent.Definition));
    Assert.Equal(1, campaign.Events.EventsOf<ScheduledEventFired>().Length);
    campaign.ClearEvents();
    campaign.AdvanceTicks(1);
    Assert.Equal(2L, campaign.Session.Tick);
    Assert.Equal(1, campaign.Events.EventsOf<TimeAdvanced>().Length);
    Assert.Equal(0, campaign.Events.EventsOf<ScheduledEventFired>().Length);
  }

  [TestCase]
  public void NegativeTickAdvancementLeavesSessionUnchanged()
  {
    using var campaign = new GeoscapeFixture();

    Assert.Throws<ArgumentOutOfRangeException>(() => campaign.AdvanceTicks(-1));

    Assert.Equal(TimeSpeed.Paused, campaign.Session.Speed);
    Assert.Equal(0L, campaign.State.Tick);
    Assert.Equal(0L, campaign.Session.Tick);
    Assert.Equal(0, campaign.Events.Count);
  }

  [TestCase]
  public void DisposalDetachesTheRecorderFromTheRetainedSession()
  {
    var campaign = new GeoscapeFixture();
    var session = campaign.Session;
    campaign.Dispose();
    campaign.Dispose();
    session.ChangeSpeed(TimeSpeed.Normal);
    session.Advance(0.1);
    Assert.Equal(0, campaign.Events.Count);
    Assert.Throws<ObjectDisposedException>(() => campaign.Advance(0.1));
  }
}
