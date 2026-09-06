using FunProject.Strategic;
using GdUnit4;

[TestSuite]
[RequireGodotRuntime]
public class GeoscapeClockTest
{
  [TestCase(TestName = "Sub-tick deltas do not advance the clock")]
  public void SubTickDeltasDoNotAdvance()
  {
    using var campaign = new GeoscapeFixture();
    campaign.ChangeSpeed(TimeSpeed.Normal); // 0.1 real seconds per tick

    campaign.Advance(0.05);
    campaign.Advance(0.009);

    Assert.Equal(0, campaign.Session.Tick);
  }

  [TestCase(TestName = "Accumulator crosses the boundary exactly once")]
  public void AccumulatorCrossesBoundaryOnce()
  {
    using var campaign = new GeoscapeFixture();
    campaign.ChangeSpeed(TimeSpeed.Normal);

    campaign.Advance(0.05);
    campaign.Advance(0.05);
    campaign.Advance(0.05);

    Assert.Equal(1, campaign.Session.Tick); // 0.15s accrued -> exactly one boundary (0.1) crossed, 0.05 carried
  }

  [TestCase(TestName = "Faster speeds tick more per real second")]
  public void FasterSpeedsTickMore()
  {
    using var normal = new GeoscapeFixture();
    normal.ChangeSpeed(TimeSpeed.Normal);
    normal.Advance(1.0);
    Assert.Equal(10, normal.Session.Tick);

    using var fast = new GeoscapeFixture();
    fast.ChangeSpeed(TimeSpeed.Fast); // 0.02s per tick
    fast.Advance(1.0);
    Assert.Equal(50, fast.Session.Tick);

    using var veryFast = new GeoscapeFixture();
    veryFast.ChangeSpeed(TimeSpeed.VeryFast); // 0.008s per tick (12.5x)
    veryFast.Advance(1.0);
    Assert.Equal(125, veryFast.Session.Tick);

    using var veryVeryFast = new GeoscapeFixture();
    veryVeryFast.ChangeSpeed(TimeSpeed.VeryVeryFast); // 0.004s per tick
    veryVeryFast.Advance(1.0);
    Assert.Equal(250, veryVeryFast.Session.Tick);
  }

  [TestCase(TestName = "Paused speed freezes advancement until changed")]
  public void PausedSpeedFreezesAdvancement()
  {
    using var campaign = new GeoscapeFixture();
    campaign.Advance(10.0); // starts paused
    Assert.Equal(0, campaign.Session.Tick);

    campaign.ChangeSpeed(TimeSpeed.Normal);
    campaign.Advance(0.1);
    Assert.Equal(1, campaign.Session.Tick);

    campaign.ChangeSpeed(TimeSpeed.Paused);
    campaign.Advance(10.0);
    Assert.Equal(1, campaign.Session.Tick);
  }

  [TestCase(TestName = "Speed changes commit no events")]
  public void SpeedChangeCommitsNothing()
  {
    using var campaign = new GeoscapeFixture();

    campaign.ChangeSpeed(TimeSpeed.Fast);

    Assert.Equal(0, campaign.Events.Count);
  }

  [TestCase(TestName = "CurrentTime advances one game-minute per tick")]
  public void CurrentTimeAdvancesOneMinutePerTick()
  {
    using var campaign = new GeoscapeFixture();
    campaign.ChangeSpeed(TimeSpeed.Normal);

    for (int i = 0; i < 6; i++)
      campaign.Advance(0.1); // one tick each: exact at the 0.1s base (no float drift)

    Assert.Equal(6, campaign.Session.Tick);
    Assert.Equal(1, campaign.Session.CurrentDay);
  }

  [TestCase(TestName = "The day counter starts at 1 and rolls on each 24 game-hours")]
  public void DayCounterRollsEveryTwentyFourHours()
  {
    using var campaign = new GeoscapeFixture(); // starts 08:00
    campaign.ChangeSpeed(TimeSpeed.VeryVeryFast);

    campaign.Advance(5.76); // 1440 ticks = 24 game-hours

    Assert.Equal(2, campaign.Session.CurrentDay);
  }

  [TestCase(TestName = "Crossing midnight does not roll the day before 24 hours elapse")]
  public void MidnightDoesNotRollTheDay()
  {
    using var campaign = new GeoscapeFixture(); // starts 08:00
    campaign.ChangeSpeed(TimeSpeed.VeryVeryFast);

    campaign.Advance(3.84); // 960 ticks = 16 game-hours → 00:00 next calendar day

    Assert.Equal(1, campaign.Session.CurrentDay);
  }
}
