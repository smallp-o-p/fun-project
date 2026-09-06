using CampaignGameState = global::FunProject.GameState.GameState;
using FunProject.Strategic;
using GdUnit4;

[TestSuite]
[RequireGodotRuntime]
public class GeoscapeScheduleTest
{
  [TestCase(TestName = "Event fires exactly at its scheduled tick")]
  public void EventFiresAtItsTick()
  {
    using var campaign = new GeoscapeFixture(timeline:
    [
      TestData.MakeScheduled(3, TestData.MakeEvent("Raid", GeoscapeEventKind.TacticalBattle)),
    ]);
    campaign.AdvanceTicks(2);
    Assert.Equal(0, campaign.Session.ActiveEvents.Count);

    campaign.AdvanceTicks(1);
    Assert.Equal(1, campaign.Session.ActiveEvents.Count);
    var active = campaign.Session.ActiveEvents[0];
    Assert.Equal("Raid", active.Definition.Title);
    Assert.Equal(3, active.OccurredTick);
  }

  [TestCase(TestName = "Entries scheduled in the past fire on the first tick")]
  public void PastEntriesFireOnFirstTick()
  {
    using var campaign = new GeoscapeFixture(timeline:
    [
      TestData.MakeScheduled(0, TestData.MakeEvent("Immediate")),
    ]);
    campaign.AdvanceTicks(1);

    Assert.Equal(1, campaign.Session.ActiveEvents.Count);
  }

  [TestCase(TestName = "Multiple entries on the same tick all fire")]
  public void MultipleEntriesSameTickAllFire()
  {
    using var campaign = new GeoscapeFixture(timeline:
    [
      TestData.MakeScheduled(2, TestData.MakeEvent("A")),
      TestData.MakeScheduled(2, TestData.MakeEvent("B")),
    ]);
    campaign.AdvanceTicks(2);

    Assert.Equal(2, campaign.Session.ActiveEvents.Count);
  }

  [TestCase(TestName = "Expired events are removed at their expiry tick")]
  public void ExpiredEventsAreRemoved()
  {
    using var campaign = new GeoscapeFixture(timeline:
    [
      TestData.MakeScheduled(2, TestData.MakeEvent("Fading", expiresAfterTicks: 3)),
    ]);
    campaign.AdvanceTicks(4);
    Assert.Equal(1, campaign.Session.ActiveEvents.Count);

    campaign.AdvanceTicks(1); // tick 5 == occurred 2 + 3

    Assert.Equal(0, campaign.Session.ActiveEvents.Count);
  }

  [TestCase(TestName = "Events without expiry stay active")]
  public void EventsWithoutExpiryStay()
  {
    using var campaign = new GeoscapeFixture(timeline:
    [
      TestData.MakeScheduled(1, TestData.MakeEvent("Everlasting")),
    ]);
    campaign.AdvanceTicks(5000);

    Assert.Equal(1, campaign.Session.ActiveEvents.Count);
  }

  [TestCase(TestName = "Firing and expiry are committed to the event stream in order")]
  public void FireAndExpiryAreCommittedInOrder()
  {
    using var campaign = new GeoscapeFixture(timeline:
    [
      TestData.MakeScheduled(1, TestData.MakeEvent("Brief", expiresAfterTicks: 1)),
    ]);
    campaign.AdvanceTicks(2);

    int timeAdvanced = 0;
    foreach (var committedEvent in campaign.Events)
      if (committedEvent is TimeAdvanced)
        timeAdvanced++;
    Assert.Equal(2, timeAdvanced);
    Assert.True(campaign.Events[0] is TimeAdvanced);
    Assert.True(campaign.Events[1] is ScheduledEventFired);
    Assert.True(campaign.Events[2] is TimeAdvanced);
    Assert.True(campaign.Events[3] is EventExpired);
  }

  [TestCase(TestName = "Expiry is baked at construction, immune to later resource edits")]
  public void ExpiryIsBakedAtConstruction()
  {
    var definition = TestData.MakeEvent("Guarded", expiresAfterTicks: 5);
    using var campaign = new GeoscapeFixture(timeline:
    [
      TestData.MakeScheduled(1, definition),
    ]);
    definition.ExpiresAfterTicks = 0; // authored resources are mutable; the bake must win

    campaign.AdvanceTicks(2); // fires at tick 1
    Assert.Equal(1, campaign.Session.ActiveEvents.Count);

    campaign.AdvanceTicks(3); // ticks 3, 4, 5 — past where the mutated 0-tick expiry would have removed it

    Assert.Equal(1, campaign.Session.ActiveEvents.Count); // baked expiry: occurred 1 + 5 = tick 6
  }

  [TestCase(TestName = "Target region resolves to an index on the active event")]
  public void TargetRegionNameResolves()
  {
    using var campaign = new GeoscapeFixture(
      regions: [TestData.MakeRegion("Northmark")],
      timeline: [TestData.MakeScheduled(1, TestData.MakeEvent("Targeted", targetRegionName: "Northmark"))]);
    campaign.AdvanceTicks(1);

    var active = campaign.Session.ActiveEvents[0];
    Assert.True(active.TargetRegionIndex.IsSome);
    Assert.Equal(0, active.TargetRegionIndex.RequireSome());
  }

  [TestCase(TestName = "A targeted event whose region name is unknown throws at construction")]
  public void UnknownTargetRegionNameThrowsAtConstruction()
  {
    Assert.Throws<System.InvalidOperationException>(() => new CampaignGameState(TestData.MakeStart(
      regions: [TestData.MakeRegion("Only")],
      timeline: [TestData.MakeScheduled(1, TestData.MakeEvent("Broken", targetRegionName: "Nowhere"))])));
  }

  [TestCase(TestName = "Duplicate region names throw at construction")]
  public void DuplicateRegionNamesThrowAtConstruction()
  {
    Assert.Throws<System.InvalidOperationException>(() => new CampaignGameState(TestData.MakeStart(
      regions: [TestData.MakeRegion("Twin"), TestData.MakeRegion("Twin")])));
  }

  [TestCase(TestName = "Timeline entries without an event throw at construction")]
  public void EntriesWithoutAnEventThrowAtConstruction()
  {
    Assert.Throws<System.InvalidOperationException>(() => new CampaignGameState(TestData.MakeStart(
      timeline: [TestData.MakeScheduled(1, null!)])));
  }

  [TestCase(TestName = "Expiry below the -1 sentinel throws at construction")]
  public void ExpiryBelowSentinelThrowsAtConstruction()
  {
    Assert.Throws<System.InvalidOperationException>(() => new CampaignGameState(TestData.MakeStart(
      timeline: [TestData.MakeScheduled(1, TestData.MakeEvent("Bad", expiresAfterTicks: -2))])));
  }

  [TestCase(TestName = "Zero expiry removes the event on its fire tick")]
  public void ZeroExpiryRemovesOnFireTick()
  {
    using var campaign = new GeoscapeFixture(timeline:
    [
      TestData.MakeScheduled(2, TestData.MakeEvent("Blink", expiresAfterTicks: 0)),
    ]);
    campaign.AdvanceTicks(2);

    Assert.Equal(0, campaign.Session.ActiveEvents.Count); // fired at tick 2, expired at 2 + 0
  }
}
