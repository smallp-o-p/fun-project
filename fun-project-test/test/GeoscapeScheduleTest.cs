using FunProject.Strategic;
using GdUnit4;
using System.Collections.Generic;

[TestSuite]
[RequireGodotRuntime]
public class GeoscapeScheduleTest
{
  private static GeoscapeSession SessionAt(int tick)
  {
    var session = GeoscapeTestFactory.MakeSession(timeline:
    [
      GeoscapeTestFactory.MakeScheduled(3, GeoscapeTestFactory.MakeEvent("Raid", GeoscapeEventKind.TacticalBattle)),
    ]);
    session.ChangeSpeed(TimeSpeed.Normal);
    session.Advance(tick * 0.1); // Normal = 0.1 real seconds per tick
    return session;
  }

  [TestCase(TestName = "Event fires exactly at its scheduled tick")]
  public void EventFiresAtItsTick()
  {
    Assert.Equal(0, SessionAt(2).ActiveEvents.Count);

    var session = SessionAt(3);
    Assert.Equal(1, session.ActiveEvents.Count);
    var active = session.ActiveEvents[0];
    Assert.Equal("Raid", active.Definition.Title);
    Assert.Equal(3, active.OccurredTick);
  }

  [TestCase(TestName = "Entries scheduled in the past fire on the first tick")]
  public void PastEntriesFireOnFirstTick()
  {
    var session = GeoscapeTestFactory.MakeSession(timeline:
    [
      GeoscapeTestFactory.MakeScheduled(0, GeoscapeTestFactory.MakeEvent("Immediate")),
    ]);
    session.ChangeSpeed(TimeSpeed.Normal);
    session.Advance(0.1);

    Assert.Equal(1, session.ActiveEvents.Count);
  }

  [TestCase(TestName = "Multiple entries on the same tick all fire")]
  public void MultipleEntriesSameTickAllFire()
  {
    var session = GeoscapeTestFactory.MakeSession(timeline:
    [
      GeoscapeTestFactory.MakeScheduled(2, GeoscapeTestFactory.MakeEvent("A")),
      GeoscapeTestFactory.MakeScheduled(2, GeoscapeTestFactory.MakeEvent("B")),
    ]);
    session.ChangeSpeed(TimeSpeed.Normal);
    session.Advance(0.2);

    Assert.Equal(2, session.ActiveEvents.Count);
  }

  [TestCase(TestName = "Expired events are removed at their expiry tick")]
  public void ExpiredEventsAreRemoved()
  {
    var session = GeoscapeTestFactory.MakeSession(timeline:
    [
      GeoscapeTestFactory.MakeScheduled(2, GeoscapeTestFactory.MakeEvent("Fading", expiresAfterTicks: 3)),
    ]);
    session.ChangeSpeed(TimeSpeed.Normal);
    session.Advance(0.4);
    Assert.Equal(1, session.ActiveEvents.Count);

    session.Advance(0.1); // tick 5 == occurred 2 + 3

    Assert.Equal(0, session.ActiveEvents.Count);
  }

  [TestCase(TestName = "Events without expiry stay active")]
  public void EventsWithoutExpiryStay()
  {
    var session = GeoscapeTestFactory.MakeSession(timeline:
    [
      GeoscapeTestFactory.MakeScheduled(1, GeoscapeTestFactory.MakeEvent("Everlasting")),
    ]);
    session.ChangeSpeed(TimeSpeed.Normal);
    session.Advance(500.0);

    Assert.Equal(1, session.ActiveEvents.Count);
  }

  [TestCase(TestName = "Firing and expiry are committed to the event stream in order")]
  public void FireAndExpiryAreCommittedInOrder()
  {
    var session = GeoscapeTestFactory.MakeSession(timeline:
    [
      GeoscapeTestFactory.MakeScheduled(1, GeoscapeTestFactory.MakeEvent("Brief", expiresAfterTicks: 1)),
    ]);
    List<IGeoscapeEvent> committed = [];
    session.ChangeSpeed(TimeSpeed.Normal);
    session.EventCommitted += committed.Add;
    session.Advance(0.2);

    int timeAdvanced = 0;
    foreach (var committedEvent in committed)
      if (committedEvent is TimeAdvanced)
        timeAdvanced++;
    Assert.Equal(2, timeAdvanced);
    Assert.True(committed[0] is TimeAdvanced);
    Assert.True(committed[1] is ScheduledEventFired);
    Assert.True(committed[2] is TimeAdvanced);
    Assert.True(committed[3] is EventExpired);
  }

  [TestCase(TestName = "Expiry is baked at construction, immune to later resource edits")]
  public void ExpiryIsBakedAtConstruction()
  {
    var definition = GeoscapeTestFactory.MakeEvent("Guarded", expiresAfterTicks: 5);
    var session = GeoscapeTestFactory.MakeSession(timeline:
    [
      GeoscapeTestFactory.MakeScheduled(1, definition),
    ]);
    definition.ExpiresAfterTicks = 0; // authored resources are mutable; the bake must win
    session.ChangeSpeed(TimeSpeed.Normal);

    session.Advance(0.2); // fires at tick 1
    Assert.Equal(1, session.ActiveEvents.Count);

    session.Advance(0.1); // tick 3
    session.Advance(0.1); // tick 4
    session.Advance(0.1); // tick 5 — past where the mutated 0-tick expiry would have removed it

    Assert.Equal(1, session.ActiveEvents.Count); // baked expiry: occurred 1 + 5 = tick 6
  }

  [TestCase(TestName = "Target region resolves to an index on the active event")]
  public void TargetRegionNameResolves()
  {
    var session = GeoscapeTestFactory.MakeSession(
      regions: [GeoscapeTestFactory.MakeRegion("Northmark")],
      timeline: [GeoscapeTestFactory.MakeScheduled(1, GeoscapeTestFactory.MakeEvent("Targeted", targetRegionName: "Northmark"))]);
    session.ChangeSpeed(TimeSpeed.Normal);
    session.Advance(0.1);

    var active = session.ActiveEvents[0];
    Assert.True(active.TargetRegionIndex.IsSome);
    Assert.Equal(0, active.TargetRegionIndex.RequireSome());
  }

  [TestCase(TestName = "A targeted event whose region name is unknown throws at construction")]
  public void UnknownTargetRegionNameThrowsAtConstruction()
  {
    Assert.Throws<System.InvalidOperationException>(() => GeoscapeTestFactory.MakeSession(
      regions: [GeoscapeTestFactory.MakeRegion("Only")],
      timeline: [GeoscapeTestFactory.MakeScheduled(1, GeoscapeTestFactory.MakeEvent("Broken", targetRegionName: "Nowhere"))]));
  }

  [TestCase(TestName = "Duplicate region names throw at construction")]
  public void DuplicateRegionNamesThrowAtConstruction()
  {
    Assert.Throws<System.InvalidOperationException>(() => GeoscapeTestFactory.MakeSession(
      regions: [GeoscapeTestFactory.MakeRegion("Twin"), GeoscapeTestFactory.MakeRegion("Twin")]));
  }

  [TestCase(TestName = "Timeline entries without an event throw at construction")]
  public void EntriesWithoutAnEventThrowAtConstruction()
  {
    Assert.Throws<System.InvalidOperationException>(() => GeoscapeTestFactory.MakeSession(
      timeline: [GeoscapeTestFactory.MakeScheduled(1, null!)]));
  }

  [TestCase(TestName = "Expiry below the -1 sentinel throws at construction")]
  public void ExpiryBelowSentinelThrowsAtConstruction()
  {
    Assert.Throws<System.InvalidOperationException>(() => GeoscapeTestFactory.MakeSession(
      timeline: [GeoscapeTestFactory.MakeScheduled(1, GeoscapeTestFactory.MakeEvent("Bad", expiresAfterTicks: -2))]));
  }

  [TestCase(TestName = "Zero expiry removes the event on its fire tick")]
  public void ZeroExpiryRemovesOnFireTick()
  {
    var session = GeoscapeTestFactory.MakeSession(timeline:
    [
      GeoscapeTestFactory.MakeScheduled(2, GeoscapeTestFactory.MakeEvent("Blink", expiresAfterTicks: 0)),
    ]);
    session.ChangeSpeed(TimeSpeed.Normal);
    session.Advance(0.2);

    Assert.Equal(0, session.ActiveEvents.Count); // fired at tick 2, expired at 2 + 0
  }
}
