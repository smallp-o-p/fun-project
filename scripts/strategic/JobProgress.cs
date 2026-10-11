using System;

namespace FunProject.Strategic;

/// <summary>Job timing. A speed change preserves the elapsed interval at its old speed.</summary>
public sealed record JobProgress
{
  public long StartedAtTick { get; }
  public long CompletesAtTick { get; }
  private readonly long _changedAtTick;
  private readonly double _remainingTicks;
  private readonly double _speed;
  private readonly double _globalSpeedBonus;

  public JobProgress(long tick, double durationTicks, double speed = 1, double globalSpeedBonus = 0)
    : this(tick, tick, durationTicks, speed, globalSpeedBonus) { }

  private JobProgress(long startedAtTick, long changedAtTick, double remainingTicks, double speed, double globalSpeedBonus)
  {
    StartedAtTick = startedAtTick;
    _changedAtTick = changedAtTick;
    _remainingTicks = remainingTicks;
    _globalSpeedBonus = globalSpeedBonus;
    _speed = EffectiveSpeed(speed, globalSpeedBonus);
    double duration = Math.Ceiling(remainingTicks / _speed);
    CompletesAtTick = checked(changedAtTick + (long)duration);
  }

  public JobProgress WithSpeed(long tick, float speed)
    => new(StartedAtTick, tick, Math.Max(0, _remainingTicks - (tick - _changedAtTick) * _speed), speed, _globalSpeedBonus);

  // Work always advances at least 1% of the base rate, even under a -100% debuff.
  internal static double EffectiveSpeed(double staffSpeed, double globalSpeedBonus)
    => Math.Clamp(staffSpeed + globalSpeedBonus, 0.01, double.MaxValue);
}
