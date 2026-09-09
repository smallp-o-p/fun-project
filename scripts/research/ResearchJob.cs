namespace FunProject.Research;

/// <summary>
/// Immutable scheduled research job: the project plus absolute start/completion ticks.
/// Carries no session reference or event subscription.
/// </summary>
public readonly record struct ResearchJob(
  ResearchProject Project, long StartedAtTick, long CompletesAtTick);
