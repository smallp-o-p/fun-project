using System;
using System.Collections.Generic;
using FunProject.GameState;
using FunProject.Items;
using FunProject.Strategic;

namespace FunProject.Engineering;

/// <summary>Campaign-owned manufacturing catalog, queries, and job progress.</summary>
public sealed class EngineeringState
{
  private readonly List<ManufacturingProject> _projects = [];
  private readonly Dictionary<EquippableItemData, ManufacturingProject> _projectsByItem = new(ReferenceEqualityComparer.Instance);

  internal EngineeringState(IReadOnlyList<EquippableItemData> initialItems)
  {
    Unlock(initialItems);
  }

  // Registers unseen item references, baking duration and stock policy at first registration.
  // Validate the whole batch first so null input never leaves a partial registry update.
  internal void Unlock(IReadOnlyList<EquippableItemData> items)
  {
    ArgumentNullException.ThrowIfNull(items);
    foreach (var item in items)
      ArgumentNullException.ThrowIfNull(item);

    foreach (var item in items)
    {
      if (_projectsByItem.ContainsKey(item)) continue;
      var project = new ManufacturingProject(item, item.ManufacturingDurationDays, item.UnlimitedStock);
      _projectsByItem.Add(item, project);
      _projects.Add(project);
    }
  }

  public Option<ManufacturingJob> ActiveJob { get; private set; }

  public IReadOnlyList<ManufacturingOption> GetManufacturingOptions(Armory armory)
  {
    ArgumentNullException.ThrowIfNull(armory);
    return _projects.AsValueEnumerable()
      .Where(project => !project.UnlimitedStock || armory.TryGetItemStock(project.Item).IsNone)
      .Select(project => new ManufacturingOption(project,
        armory.TryGetItemStock(project.Item).Match(stock => stock.Remaining, () => 0)))
      .ToImmutableList();
  }

  public Either<ManufacturingStartFailure, ManufacturingProject> ResolveManufacturing(EquippableItemData item, Armory armory)
  {
    ArgumentNullException.ThrowIfNull(item);
    ArgumentNullException.ThrowIfNull(armory);
    if (!_projectsByItem.TryGetValue(item, out var project))
      return ManufacturingStartFailure.UnknownItem;
    if (ActiveJob.IsSome)
      return ManufacturingStartFailure.Busy;
    if (project.UnlimitedStock && armory.TryGetItemStock(item).IsSome)
      return ManufacturingStartFailure.AlreadyAvailable;
    return project;
  }

  // GeoscapeSession schedules the resolved project before starting it.
  internal ManufacturingStarted Start(ManufacturingJob job)
  {
    ActiveJob = Some(job);
    return new ManufacturingStarted(job);
  }

  internal Option<ManufacturingCompleted> CompleteIfDue(long tick)
    => ActiveJob.Filter(job => job.CompletesAtTick <= tick).Map(job =>
    {
      ActiveJob = None;
      return new ManufacturingCompleted(job);
    });
}
