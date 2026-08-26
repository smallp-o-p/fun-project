using FunProject.Strategic;
using GdUnit4;
using System.Collections.Generic;

[TestSuite]
[RequireGodotRuntime]
public class GeoscapeClockTest
{
  [TestCase(TestName = "Sub-tick deltas do not advance the clock")]
  public void SubTickDeltasDoNotAdvance()
  {
    var session = GeoscapeTestFactory.MakeSession();
    session.ChangeSpeed(TimeSpeed.Normal); // 0.1 real seconds per tick

    session.Advance(0.05);
    session.Advance(0.009);

    Assert.Equal(0, session.Tick);
  }

  [TestCase(TestName = "Accumulator crosses the boundary exactly once")]
  public void AccumulatorCrossesBoundaryOnce()
  {
    var session = GeoscapeTestFactory.MakeSession();
    session.ChangeSpeed(TimeSpeed.Normal);

    session.Advance(0.05);
    session.Advance(0.05);
    session.Advance(0.05);

    Assert.Equal(1, session.Tick); // 0.15s accrued -> exactly one boundary (0.1) crossed, 0.05 carried
  }

  [TestCase(TestName = "Faster speeds tick more per real second")]
  public void FasterSpeedsTickMore()
  {
    var normal = GeoscapeTestFactory.MakeSession();
    normal.ChangeSpeed(TimeSpeed.Normal);
    normal.Advance(1.0);
    Assert.Equal(10, normal.Tick);

    var fast = GeoscapeTestFactory.MakeSession();
    fast.ChangeSpeed(TimeSpeed.Fast); // 0.02s per tick
    fast.Advance(1.0);
    Assert.Equal(50, fast.Tick);

    var veryFast = GeoscapeTestFactory.MakeSession();
    veryFast.ChangeSpeed(TimeSpeed.VeryFast); // 0.008s per tick (12.5x)
    veryFast.Advance(1.0);
    Assert.Equal(125, veryFast.Tick);

    var veryVeryFast = GeoscapeTestFactory.MakeSession();
    veryVeryFast.ChangeSpeed(TimeSpeed.VeryVeryFast); // 0.004s per tick
    veryVeryFast.Advance(1.0);
    Assert.Equal(250, veryVeryFast.Tick);
  }

  [TestCase(TestName = "Paused speed freezes advancement until changed")]
  public void PausedSpeedFreezesAdvancement()
  {
    var session = GeoscapeTestFactory.MakeSession();
    session.Advance(10.0); // starts paused
    Assert.Equal(0, session.Tick);

    session.ChangeSpeed(TimeSpeed.Normal);
    session.Advance(0.1);
    Assert.Equal(1, session.Tick);

    session.ChangeSpeed(TimeSpeed.Paused);
    session.Advance(10.0);
    Assert.Equal(1, session.Tick);
  }

  [TestCase(TestName = "Speed changes commit no events")]
  public void SpeedChangeCommitsNothing()
  {
    var session = GeoscapeTestFactory.MakeSession();
    List<IGeoscapeEvent> committed = [];
    session.EventCommitted += committed.Add;

    session.ChangeSpeed(TimeSpeed.Fast);

    Assert.Equal(0, committed.Count);
  }

  [TestCase(TestName = "CurrentTime advances one game-minute per tick")]
  public void CurrentTimeAdvancesOneMinutePerTick()
  {
    var session = GeoscapeTestFactory.MakeSession();
    session.ChangeSpeed(TimeSpeed.Normal);

    for (int i = 0; i < 6; i++)
      session.Advance(0.1); // one tick each: exact at the 0.1s base (no float drift)

    Assert.Equal(6, session.Tick);
    Assert.Equal(session.StartTime + System.TimeSpan.FromMinutes(6), session.CurrentTime);
    Assert.Equal(1, session.CurrentDay);
  }

  [TestCase(TestName = "The day counter starts at 1 and rolls on each 24 game-hours")]
  public void DayCounterRollsEveryTwentyFourHours()
  {
    var session = GeoscapeTestFactory.MakeSession(); // starts 08:00
    session.ChangeSpeed(TimeSpeed.VeryVeryFast);

    session.Advance(5.76); // 1440 ticks = 24 game-hours

    Assert.Equal(session.StartTime + System.TimeSpan.FromDays(1), session.CurrentTime);
    Assert.Equal(2, session.CurrentDay);
  }

  [TestCase(TestName = "Crossing midnight does not roll the day before 24 hours elapse")]
  public void MidnightDoesNotRollTheDay()
  {
    var session = GeoscapeTestFactory.MakeSession(); // starts 08:00
    session.ChangeSpeed(TimeSpeed.VeryVeryFast);

    session.Advance(3.84); // 960 ticks = 16 game-hours → 00:00 next calendar day

    Assert.Equal(session.StartTime.Date + System.TimeSpan.FromDays(1), session.CurrentTime.Date);
    Assert.Equal(1, session.CurrentDay); // still Day 1 until 24 hours elapse
  }
}
