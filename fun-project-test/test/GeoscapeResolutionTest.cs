using FunProject.Strategic;
using GdUnit4;

[TestSuite]
[RequireGodotRuntime]
public class GeoscapeResolutionTest
{
  private static (GeoscapeSession Session, GeoscapeEvent Active) SessionWithFiredEvent()
  {
    var session = GeoscapeTestFactory.MakeSession(timeline:
    [
      GeoscapeTestFactory.MakeScheduled(1, GeoscapeTestFactory.MakeEvent("Incident")),
    ]);
    session.ChangeSpeed(TimeSpeed.Normal);
    session.Advance(0.1);
    return (session, session.ActiveEvents[0]);
  }

  [TestCase(TestName = "Opening a resolution freezes time until completed")]
  public void OpenFreezesTimeUntilCompleted()
  {
    var (session, active) = SessionWithFiredEvent();

    session.OpenResolution(active);
    session.Advance(100.0);
    Assert.Equal(1, session.Tick); // frozen
    Assert.True(session.PendingResolution.IsSome);

    session.CompleteResolution(ResolutionOutcome.Acknowledged);
    session.Advance(0.1);
    Assert.Equal(2, session.Tick); // resumed
  }

  [TestCase(TestName = "Completing removes the event and clears pending")]
  public void CompleteRemovesEventAndClearsPending()
  {
    var (session, active) = SessionWithFiredEvent();

    session.OpenResolution(active);
    session.CompleteResolution(ResolutionOutcome.Engaged);

    Assert.True(session.PendingResolution.IsNone);
    Assert.Equal(0, session.ActiveEvents.Count);
  }

  [TestCase(TestName = "Opening while pending throws")]
  public void DoubleOpenThrows()
  {
    var (session, active) = SessionWithFiredEvent();
    session.OpenResolution(active);

    Assert.Throws<System.InvalidOperationException>(() => session.OpenResolution(active));
  }

  [TestCase(TestName = "Completing without pending throws")]
  public void CompleteWithoutOpenThrows()
  {
    var (session, _) = SessionWithFiredEvent();

    Assert.Throws<System.InvalidOperationException>(() => session.CompleteResolution(ResolutionOutcome.Declined));
  }

  [TestCase(TestName = "Open and complete are committed to the event stream")]
  public void LifecycleIsCommitted()
  {
    var (session, active) = SessionWithFiredEvent();
    var committed = new System.Collections.Generic.List<IGeoscapeEvent>();
    session.EventCommitted += committed.Add;
    // Subscribers of ResolutionEventClosed observe post-state: the resolved event must be
    // gone from ActiveEvents AND the resolution itself must already be closed when the
    // event is committed.
    int activeCountOnClosed = -1;
    bool pendingOnClosed = true;
    session.EventCommitted += geoscapeEvent =>
    {
      if (geoscapeEvent is ResolutionEventClosed)
      {
        activeCountOnClosed = session.ActiveEvents.Count;
        pendingOnClosed = session.PendingResolution.IsSome;
      }
    };

    session.OpenResolution(active);
    session.CompleteResolution(ResolutionOutcome.Declined);

    Assert.Equal(2, committed.Count);
    Assert.True(committed[0] is ResolutionEventOpened opened && opened.Pending.Event == active);
    Assert.True(committed[1] is ResolutionEventClosed closed
      && closed.Resolved.Event == active
      && closed.SelectedOutcome == ResolutionOutcome.Declined);
    Assert.Equal(0, activeCountOnClosed);
    Assert.False(pendingOnClosed);
  }
}
