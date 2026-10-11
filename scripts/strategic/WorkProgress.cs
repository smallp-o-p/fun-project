using System;
using CampaignGameState = global::FunProject.GameState.GameState;

namespace FunProject.Strategic;

/// <summary>Campaign-owned work in fixed-point units. Estimates settle a copy; rate
/// changes must settle the elapsed interval at its old rate before installing the new rate.</summary>
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
public readonly record struct WorkProgress
{
  public const long RateScale = 10_000;

  public long RequiredWorkUnits { get; }
  public long RemainingWorkUnits { get; }
  public long LastCreditedTick { get; }

  private WorkProgress(long requiredWorkUnits, long remainingWorkUnits, long lastCreditedTick)
  {
    RequiredWorkUnits = requiredWorkUnits;
    RemainingWorkUnits = remainingWorkUnits;
    LastCreditedTick = lastCreditedTick;
  }

  public static WorkProgress Start(uint durationDays, long tick)
  {
    ArgumentOutOfRangeException.ThrowIfNegative(tick);
    long required = checked(CampaignGameState.TicksFromDays(durationDays) * RateScale);
    return new WorkProgress(required, required, tick);
  }

  public WorkProgress SettleTo(long tick, long rateUnits)
  {
    ArgumentOutOfRangeException.ThrowIfLessThan(tick, LastCreditedTick);
    ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rateUnits);
    long elapsed = checked(tick - LastCreditedTick);
    // Compare before multiplying: an arbitrary interval/rate can overflow even though
    // it simply exhausts the remaining work. A nonfinishing product is bounded by it.
    long remaining = elapsed >= CeilingTicks(RemainingWorkUnits, rateUnits)
      ? 0
      : RemainingWorkUnits - checked(elapsed * rateUnits);
    return new WorkProgress(RequiredWorkUnits, remaining, tick);
  }

  public long RemainingTicks(long tick, long rateUnits)
    => CeilingTicks(SettleTo(tick, rateUnits).RemainingWorkUnits, rateUnits);

  public long CompletionTick(long tick, long rateUnits)
    => checked(tick + RemainingTicks(tick, rateUnits));

  private static long CeilingTicks(long workUnits, long rateUnits)
    => workUnits / rateUnits + (workUnits % rateUnits == 0 ? 0 : 1);
}
