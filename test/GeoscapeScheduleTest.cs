using CampaignGameState = global::FunProject.GameState.GameState;
using FunProject.GameState;
using FunProject.Strategic;
using GdUnit4;

[TestSuite]
[RequireGodotRuntime]
public class GeoscapeScheduleTest
{
  [TestCase(3, "Raid", GeoscapeEventKind.TacticalBattle, 2, 3L)]
  [TestCase(0, "Immediate", GeoscapeEventKind.Plot, 0, 1L)]
  public void ScheduledEventFiresAtFirstDueAdvancingTick(
    int atTick, string title, GeoscapeEventKind kind, int beforeTicks, long expectedOccurredTick)
  {
    using var campaign = new GeoscapeFixture(timeline:
    [
      TestData.MakeScheduled(atTick, TestData.MakeEvent(title, kind)),
    ]);

    if (beforeTicks > 0)
    {
      campaign.AdvanceTicks(beforeTicks);
      Assert.Equal(0, campaign.Session.ActiveEvents.Count);
    }

    campaign.AdvanceTicks(1);
    Assert.Equal(1, campaign.Session.ActiveEvents.Count);
    var active = campaign.Session.ActiveEvents[0];
    Assert.Equal(title, active.Definition.Title);
    // The active record carries the actual firing tick, so the past row reads 1, not its authored 0.
    Assert.Equal(expectedOccurredTick, active.OccurredTick);
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

  [TestCase(2, "Fading", 3, 4, 1, 1, 0)]
  [TestCase(1, "Everlasting", -1, 5000, 1, 0, 1)]
  [TestCase(2, "Blink", 0, 2, 0, 0, 0)]
  public void ScheduledEventLifetimeRespectsExpiry(
    int atTick, string title, int expiry, int firstAdvance, int firstCount, int extraTicks, int finalCount)
  {
    using var campaign = new GeoscapeFixture(timeline:
    [
      TestData.MakeScheduled(atTick, TestData.MakeEvent(title, expiresAfterTicks: expiry)),
    ]);

    campaign.AdvanceTicks(firstAdvance);
    Assert.Equal(firstCount, campaign.Session.ActiveEvents.Count);
    if (extraTicks > 0)
    {
      campaign.AdvanceTicks(extraTicks); // expiry is inclusive: e.g. tick 5 == occurred 2 + 3
      Assert.Equal(finalCount, campaign.Session.ActiveEvents.Count);
    }
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

  [TestCase(TestName = "Malformed schedule authoring fails campaign construction")]
  public void MalformedScheduleAuthoringFailsConstruction()
  {
    (string Case, CampaignStartData Start)[] malformed =
    [
      ("unknown target region", TestData.MakeStart(
        regions: [TestData.MakeRegion("Only")],
        timeline: [TestData.MakeScheduled(1, TestData.MakeEvent("Broken", targetRegionName: "Nowhere"))])),
      ("duplicate region names", TestData.MakeStart(
        regions: [TestData.MakeRegion("Twin"), TestData.MakeRegion("Twin")],
        timeline: [])),
      ("timeline entry without an event", TestData.MakeStart(
        regions: [],
        timeline: [TestData.MakeScheduled(1, null!)])),
      ("expiry below the -1 sentinel", TestData.MakeStart(
        regions: [],
        timeline: [TestData.MakeScheduled(1, TestData.MakeEvent("Bad", expiresAfterTicks: -2))])),
    ];
    foreach ((string name, CampaignStartData start) in malformed)
      Assert.Throws<System.InvalidOperationException>(() => new CampaignGameState(start), name);
  }

  [TestCase(TestName = "Tactical events retain a battle seed; other events carry none")]
  public void TacticalEventRetainsBattleSeed()
  {
    using var campaign = new GeoscapeFixture(timeline:
    [
      TestData.MakeScheduled(1, TestData.MakeEvent("Raid", GeoscapeEventKind.TacticalBattle)),
      TestData.MakeScheduled(1, TestData.MakeEvent("Broadcast")),
    ]);

    campaign.AdvanceTicks(1);

    Assert.Equal(2, campaign.Session.ActiveEvents.Count);
    foreach (GeoscapeEvent active in campaign.Session.ActiveEvents)
    {
      if (active.Definition.Kind == GeoscapeEventKind.TacticalBattle)
        Assert.True(active.BattleSeed.IsSome, "Tactical events must retain a battle seed.");
      else
        Assert.True(active.BattleSeed.IsNone, "Non-tactical events must not carry a battle seed.");
    }
  }

  [TestCase(TestName = "A fired tactical event keeps its battle seed across a session rebuild")]
  public void BattleSeedSurvivesSessionRebuild()
  {
    using var campaign = new GeoscapeFixture(timeline:
    [
      TestData.MakeScheduled(1, TestData.MakeEvent("Raid", GeoscapeEventKind.TacticalBattle)),
    ]);
    campaign.AdvanceTicks(1);
    int seed = campaign.ActiveEvent.BattleSeed.RequireSome();

    var rebuilt = new GeoscapeSession(campaign.State);

    Assert.Equal(1, rebuilt.ActiveEvents.Count);
    Assert.True(rebuilt.ActiveEvents[0].BattleSeed.IsSome);
    Assert.Equal(seed, rebuilt.ActiveEvents[0].BattleSeed.RequireSome());
  }
}
