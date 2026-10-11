using FunProject.Strategic;

namespace FunProject.Engineering;

public readonly record struct ManufacturingJob(ManufacturingRecipe Project, long StartedAtTick, WorkProgress Work)
{
  public long RateUnits { get; init; } = WorkProgress.RateScale;
  public long CompletesAtTick => Work.CompletionTick(Work.LastCreditedTick, RateUnits);
}
