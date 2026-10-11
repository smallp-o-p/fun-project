using FunProject.Strategic;

namespace FunProject.Engineering;

public readonly record struct ManufacturingJob(ManufacturingRecipe Project, JobProgress Progress)
{
  public ManufacturingJob(ManufacturingRecipe project, long startedAtTick, long completesAtTick)
    : this(project, new JobProgress(startedAtTick, completesAtTick - startedAtTick)) { }

  public long StartedAtTick => Progress.StartedAtTick;
  public long CompletesAtTick => Progress.CompletesAtTick;
  internal ManufacturingJob WithSpeed(long tick, float speed) => this with { Progress = Progress.WithSpeed(tick, speed) };
}
