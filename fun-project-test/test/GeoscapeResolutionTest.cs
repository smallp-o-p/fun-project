using FunProject.Strategic;
using GdUnit4;

[TestSuite]
[RequireGodotRuntime]
public class GeoscapeResolutionTest
{
  [TestCase(TestName = "Opening a resolution freezes time until completed")]
  public void OpenFreezesTimeUntilCompleted()
  {
    using var campaign = GeoscapeFixture.WithFiredEvent(TestData.MakeEvent("Incident"));

    campaign.OpenResolution(campaign.ActiveEvent);
    campaign.Advance(100.0);
    Assert.Equal(1, campaign.Session.Tick); // frozen
    Assert.True(campaign.Session.PendingResolution.IsSome);

    campaign.CompleteResolution(ResolutionOutcome.Acknowledged);
    campaign.Advance(0.1);
    Assert.Equal(2, campaign.Session.Tick); // resumed
  }

  [TestCase(TestName = "Completing removes the event and clears pending")]
  public void CompleteRemovesEventAndClearsPending()
  {
    using var campaign = GeoscapeFixture.WithFiredEvent(TestData.MakeEvent("Incident"));

    campaign.OpenResolution(campaign.ActiveEvent);
    campaign.CompleteResolution(ResolutionOutcome.Engaged);

    Assert.True(campaign.Session.PendingResolution.IsNone);
    Assert.Equal(0, campaign.Session.ActiveEvents.Count);
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

  [TestCase(TestName = "Open and complete are committed to the event stream")]
  public void LifecycleIsCommitted()
  {
    using var campaign = GeoscapeFixture.WithFiredEvent(TestData.MakeEvent("Incident"));
    var active = campaign.ActiveEvent;
    campaign.ClearEvents();
    // Subscribers of ResolutionEventClosed observe post-state: the resolved event must be
    // gone from ActiveEvents AND the resolution itself must already be closed when the
    // event is committed.
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
    campaign.CompleteResolution(ResolutionOutcome.Declined);
    campaign.Session.EventCommitted -= Inspector;

    Assert.Equal(2, campaign.Events.Count);
    Assert.True(campaign.Events[0] is ResolutionEventOpened opened && opened.Pending.Event == active);
    Assert.True(campaign.Events[1] is ResolutionEventClosed closed
      && closed.Resolved.Event == active
      && closed.SelectedOutcome == ResolutionOutcome.Declined);
    Assert.Equal(0, activeCountOnClosed);
    Assert.False(pendingOnClosed);
  }
}
