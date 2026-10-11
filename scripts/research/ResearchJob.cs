using FunProject.Strategic;

namespace FunProject.Research;

/// <summary>
/// Campaign work and its current rate; the deadline is a forecast, not stored progress.
/// Carries no session reference or event subscription.
/// </summary>
public readonly record struct ResearchJob(
  ResearchProject Project, long StartedAtTick, WorkProgress Work)
{
  public long RateUnits { get; init; } = WorkProgress.RateScale;
  public long CompletesAtTick => Work.CompletionTick(Work.LastCreditedTick, RateUnits);
}
