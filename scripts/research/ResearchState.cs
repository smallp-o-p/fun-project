using System;
using System.Collections.Generic;
using FunProject.Items;
using FunProject.Strategic;
using CampaignGameState = global::FunProject.GameState.GameState;

namespace FunProject.Research;

/// <summary>
/// Campaign-owned research catalog, availability, and job progress: an ordered,
/// reference-deduplicated catalog with captured project definitions, completed projects,
/// and the single active job. Availability is evaluated on demand against the supplied
/// campaign; the state retains no campaign, session, Engineering, or Armory reference.
/// </summary>
public sealed class ResearchState
{
  internal sealed record Definition(ResearchProject Project, uint DurationDays,
    ResearchCondition Condition, IReadOnlyList<EquippableItemData> ManufacturingUnlocks);

  private readonly List<Definition> _definitions = [];
  private readonly Dictionary<ResearchProject, Definition> _byProject = new(ReferenceEqualityComparer.Instance);
  private readonly SysColGeneric.HashSet<ResearchProject> _completedProjects = new(ReferenceEqualityComparer.Instance);

  internal ResearchState(IReadOnlyList<ResearchProject> projects)
  {
    ArgumentNullException.ThrowIfNull(projects);
    foreach (var project in projects)
    {
      ArgumentNullException.ThrowIfNull(project);
      if (_byProject.ContainsKey(project)) continue;
      ArgumentNullException.ThrowIfNull(project.Condition);
      ArgumentNullException.ThrowIfNull(project.ManufacturingUnlocks);
      project.Condition.CheckAuthoring();
      foreach (var item in project.ManufacturingUnlocks)
        ArgumentNullException.ThrowIfNull(item);
      var definition = new Definition(project, project.DurationDays, project.Condition,
        project.ManufacturingUnlocks.AsValueEnumerable().ToImmutableList());
      _definitions.Add(definition);
      _byProject.Add(project, definition);
    }
  }

  public IReadOnlyList<ResearchProject> Projects
    => _definitions.AsValueEnumerable().Select(definition => definition.Project).ToImmutableList();

  public Option<ResearchJob> ActiveJob { get; private set; }

  public bool IsCompleted(ResearchProject project)
  {
    ArgumentNullException.ThrowIfNull(project);
    return _completedProjects.Contains(project);
  }

  public IReadOnlyList<ResearchProject> GetAvailableProjects(CampaignGameState state)
  {
    ArgumentNullException.ThrowIfNull(state);
    ResearchProject? activeProject = null;
    ActiveJob.IfSome(job => activeProject = job.Project);
    return _definitions.AsValueEnumerable()
      .Where(definition => !_completedProjects.Contains(definition.Project)
        && !ReferenceEquals(activeProject, definition.Project)
        && definition.Condition.IsMet(state))
      .Select(definition => definition.Project).ToImmutableList();
  }

  internal Either<ResearchStartFailure, Definition> ResolveResearch(
    ResearchProject project, CampaignGameState state)
  {
    ArgumentNullException.ThrowIfNull(project);
    ArgumentNullException.ThrowIfNull(state);
    if (!_byProject.TryGetValue(project, out var definition)) return ResearchStartFailure.UnknownProject;
    if (ActiveJob.IsSome) return ResearchStartFailure.Busy;
    if (_completedProjects.Contains(project)) return ResearchStartFailure.AlreadyCompleted;
    if (!definition.Condition.IsMet(state)) return ResearchStartFailure.Locked;
    return definition;
  }

  // Trusted lifecycle operation, matching Engineering: production calls it only after
  // resolution and scheduling.
  internal ResearchStarted Start(ResearchJob job)
  {
    ActiveJob = Some(job);
    return new ResearchStarted(job);
  }

  internal Option<ResearchCompleted> CompleteIfDue(long tick)
    => ActiveJob.Filter(job => job.CompletesAtTick <= tick).Map(job =>
    {
      var outputs = _byProject[job.Project].ManufacturingUnlocks;
      ActiveJob = None;
      _completedProjects.Add(job.Project);
      return new ResearchCompleted(job, outputs);
    });
}
