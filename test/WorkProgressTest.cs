using System;
using FunProject.Strategic;
using GdUnit4;

namespace FunProject.Tests;

[TestSuite]
[RequireGodotRuntime]
public class WorkProgressTest
{
  [TestCase]
  public static void FractionalWorkAndMidpointRatePreserveEarnedWork()
  {
    var work = WorkProgress.Start(1, 0).SettleTo(720, 10_000);
    Assert.Equal(7_200_000L, work.RemainingWorkUnits);
    Assert.Equal(480L, work.RemainingTicks(720, 15_000));
    Assert.Equal(1200L, work.CompletionTick(720, 15_000));
    Assert.Equal(work, work.SettleTo(720, 15_000));
    Assert.Equal(7_185_000L, work.SettleTo(721, 15_000).RemainingWorkUnits);
    Assert.Equal(14_378_000L,
      WorkProgress.Start(1, 0).SettleTo(1, 11_000).SettleTo(2, 11_000).RemainingWorkUnits);
  }

  [TestCase]
  public static void MaximumWorkAndLargeElapsedIntervalsDoNotOverflow()
  {
    var work = WorkProgress.Start(uint.MaxValue, 0);
    Assert.Equal(61_847_529_048_000_000L, work.RequiredWorkUnits);
    Assert.Equal(6_184_752_904_800L, work.RemainingTicks(0, 10_000));
    var completed = work.SettleTo(long.MaxValue, long.MaxValue);
    Assert.Equal(0L, completed.RemainingWorkUnits);
    Assert.Equal(long.MaxValue, completed.LastCreditedTick);
    Assert.Equal(0L, completed.RemainingTicks(long.MaxValue, 10_000));
  }

  [TestCase]
  public static void AOneUnitRemainderStillNeedsOneFutureTick()
  {
    var work = WorkProgress.Start(1, 7).SettleTo(8, 14_399_999);
    Assert.Equal(1L, work.RemainingWorkUnits);
    Assert.Equal(1L, work.RemainingTicks(8, long.MaxValue));
    Assert.Equal(9L, work.CompletionTick(8, long.MaxValue));
    Assert.Equal(0L, work.SettleTo(9, long.MaxValue).RemainingWorkUnits);
  }

  [TestCase]
  public static void EstimatesAreNonMutatingAndZeroWorkIsImmediatelyDue()
  {
    var work = WorkProgress.Start(1, 20);
    Assert.Equal(1308L, work.RemainingTicks(22, 11_000));
    Assert.Equal(14_400_000L, work.RemainingWorkUnits);
    Assert.Equal(20L, work.LastCreditedTick);
    Assert.Equal(0L, WorkProgress.Start(0, 20).RemainingTicks(20, 10_000));
  }

  [TestCase]
  public static void InvalidRatesBackwardsSettlementAndForecastOverflowThrow()
  {
    var work = WorkProgress.Start(1, 20);
    Assert.Throws<ArgumentOutOfRangeException>(() => work.SettleTo(19, 10_000));
    Assert.Throws<ArgumentOutOfRangeException>(() => work.RemainingTicks(19, 10_000));
    Assert.Throws<ArgumentOutOfRangeException>(() => work.SettleTo(20, 0));
    Assert.Throws<ArgumentOutOfRangeException>(() => work.RemainingTicks(20, -1));
    Assert.Throws<OverflowException>(() => WorkProgress.Start(1, long.MaxValue)
      .CompletionTick(long.MaxValue, 10_000));
  }
}
