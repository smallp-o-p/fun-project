using FunProject.Strategic;
using GdUnit4;

[TestSuite]
[RequireGodotRuntime]
public class GeoscapeClockTest
{
  [TestCase(0.009, false, 0L)]
  [TestCase(0.05, true, 1L)]
  public void AccumulatorPreservesSubTickRemainder(double secondDelta, bool thirdHalfTick, long expectedTick)
  {
    using var campaign = new GeoscapeFixture();
    campaign.ChangeSpeed(TimeSpeed.Normal); // 0.1 real seconds per tick

    campaign.Advance(0.05);
    campaign.Advance(secondDelta);
    if (thirdHalfTick)
      campaign.Advance(0.05); // 0.15s accrued: exactly one boundary (0.1) crossed, 0.05 carried

    Assert.Equal(expectedTick, campaign.Session.Tick);
  }

  // Seconds per tick: Normal 0.1, Fast 0.02, VeryFast 0.008 (12.5x), VeryVeryFast 0.004.
  [TestCase(TimeSpeed.Normal, 10)]
  [TestCase(TimeSpeed.Fast, 50)]
  [TestCase(TimeSpeed.VeryFast, 125)]
  [TestCase(TimeSpeed.VeryVeryFast, 250)]
  public void FasterSpeedsTickMore(TimeSpeed speed, int expectedTicks)
  {
    using var campaign = new GeoscapeFixture();

    campaign.ChangeSpeed(speed);
    // A speed change itself commits no events, before any advancement.
    Assert.Equal(0, campaign.Events.Count);
    campaign.Advance(1.0);

    Assert.Equal(expectedTicks, campaign.Session.Tick);
  }

  [TestCase(5.76, 2)] // 1440 ticks = 24 game-hours: the day rolls on elapsed hours
  [TestCase(3.84, 1)] // 960 ticks = 16 game-hours: elapsed hours, not calendar midnight, roll the day
  public void ElapsedTimeControlsDayCounter(double delta, int expectedDay)
  {
    using var campaign = new GeoscapeFixture();
    campaign.ChangeSpeed(TimeSpeed.VeryVeryFast);

    campaign.Advance(delta);

    Assert.Equal(expectedDay, campaign.Session.CurrentDay);
  }
}
