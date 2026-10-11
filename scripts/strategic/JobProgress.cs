using System;

namespace FunProject.Strategic;

/// <summary>Job timing. A speed change preserves the elapsed interval at its old speed.</summary>
public sealed record JobProgress
{
  public long StartedAtTick { get; }
  public long CompletesAtTick { get; }
  private readonly long _changedAtTick;
  private readonly double _remainingTicks;
  private readonly float _speed;

  public JobProgress(long tick, double durationTicks, float speed = 1)
    : this(tick, tick, durationTicks, speed) { }

  private JobProgress(long startedAtTick, long changedAtTick, double remainingTicks, float speed)
  {
    StartedAtTick = startedAtTick;
    _changedAtTick = changedAtTick;
    _remainingTicks = remainingTicks;
    _speed = speed;
    double duration = Math.Ceiling(remainingTicks / speed);
    CompletesAtTick = checked(changedAtTick + (long)duration);
  }

  public JobProgress WithSpeed(long tick, float speed)
    => new(StartedAtTick, tick, Math.Max(0, _remainingTicks - (tick - _changedAtTick) * (double)_speed), speed);
}
