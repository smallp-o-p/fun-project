namespace FunProject.Strategic;

public enum TimeSpeed
{
  Paused = -1,
  Normal = 0,
  Fast = 1,
  VeryFast = 2,
  VeryVeryFast = 3,
}

public enum ResolutionOutcome
{
  Acknowledged,
  Engaged,
  Declined,
}

public sealed record PendingResolution(GeoscapeEvent Event);
