using FunProject.Strategic;

namespace FunProject.Research;

public readonly record struct ResearchJob(ResearchProject Project, JobProgress Progress)
{
  public ResearchJob(ResearchProject project, long startedAtTick, long completesAtTick)
    : this(project, new JobProgress(startedAtTick, completesAtTick - startedAtTick)) { }

  public long StartedAtTick => Progress.StartedAtTick;
  public long CompletesAtTick => Progress.CompletesAtTick;
  internal ResearchJob WithSpeed(long tick, float speed) => this with { Progress = Progress.WithSpeed(tick, speed) };
}
