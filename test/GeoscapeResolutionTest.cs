using FunProject.Strategic;
using GdUnit4;

[TestSuite]
[RequireGodotRuntime]
public class GeoscapeResolutionTest
{
  // Subscribers of ResolutionEventClosed observe post-state: the resolved event must be
  // gone from ActiveEvents AND the resolution itself must already be closed when the
  // event is committed.
  [TestCase(ResolutionOutcome.Acknowledged)]
  [TestCase(ResolutionOutcome.Engaged)]
  [TestCase(ResolutionOutcome.Declined)]
  public void ResolutionLifecycleCommitsPostState(ResolutionOutcome outcome)
  {
    using var campaign = GeoscapeFixture.WithFiredEvent(TestData.MakeEvent("Incident"));
    var active = campaign.ActiveEvent;
    campaign.ClearEvents();

    int activeCountOnClosed = -1;
    bool pendingOnClosed = true;
    void Inspector(IGeoscapeEvent geoscapeEvent)
    {
      if (geoscapeEvent is ResolutionEventClosed)
      {
        activeCountOnClosed = campaign.Session.ActiveEvents.Count;
        pendingOnClosed = campaign.Session.PendingResolution.IsSome;
      }
    }

    campaign.Session.EventCommitted += Inspector;
    campaign.OpenResolution(active);
    campaign.Advance(100.0);
    Assert.Equal(1, campaign.Session.Tick); // frozen
    Assert.True(campaign.Session.PendingResolution.IsSome);

    campaign.CompleteResolution(outcome);
    campaign.Session.EventCommitted -= Inspector;

    Assert.True(campaign.Session.PendingResolution.IsNone);
    Assert.Equal(0, campaign.Session.ActiveEvents.Count);
    Assert.Equal(2, campaign.Events.Count);
    Assert.True(campaign.Events[0] is ResolutionEventOpened opened && opened.Pending.Event == active);
    Assert.True(campaign.Events[1] is ResolutionEventClosed closed
      && closed.Resolved.Event == active
      && closed.SelectedOutcome == outcome);
    Assert.Equal(0, activeCountOnClosed);
    Assert.False(pendingOnClosed);

    campaign.Advance(0.1);
    Assert.Equal(2, campaign.Session.Tick); // resumed
  }

  [TestCase(TestName = "Opening while pending throws")]
  public void DoubleOpenThrows()
  {
    using var campaign = GeoscapeFixture.WithFiredEvent(TestData.MakeEvent("Incident"));
    var active = campaign.ActiveEvent;
    campaign.OpenResolution(active);

    Assert.Throws<System.InvalidOperationException>(() => campaign.OpenResolution(active));
  }

  [TestCase(TestName = "Completing without pending throws")]
  public void CompleteWithoutOpenThrows()
  {
    using var campaign = GeoscapeFixture.WithFiredEvent(TestData.MakeEvent("Incident"));

    Assert.Throws<System.InvalidOperationException>(() => campaign.CompleteResolution(ResolutionOutcome.Declined));
  }
}
