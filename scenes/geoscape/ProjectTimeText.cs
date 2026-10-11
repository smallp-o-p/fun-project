using System;
using FunProject.Strategic;

namespace FunProject.Scenes;

public static class ProjectTimeText
{
  public static string Duration(uint days, double speed)
  {
    long ticks = new JobProgress(0, GameState.GameState.TicksFromDays(days), speed).CompletesAtTick;
    long ticksPerDay = GameState.GameState.TicksFromDays(1);
    return ticks % ticksPerDay == 0 ? $"Duration: {ticks / ticksPerDay}d" : $"Duration: {Remaining(ticks, 0)}";
  }

  public static string Remaining(long completesAtTick, long tick)
  {
    long ticks = completesAtTick <= tick ? 0 : completesAtTick - tick;
    long seconds = checked(ticks * GeoscapeSession.TickGameSeconds);
    long days = seconds / (TimeSpan.TicksPerDay / TimeSpan.TicksPerSecond);
    long hours = seconds / (TimeSpan.TicksPerHour / TimeSpan.TicksPerSecond) % 24;
    long minutes = seconds / (TimeSpan.TicksPerMinute / TimeSpan.TicksPerSecond) % 60;
    if (days > 0)
      return $"{days}d {hours}h {minutes}m";
    if (hours > 0)
      return $"{hours}h {minutes}m";
    return $"{minutes}m";
  }
}
