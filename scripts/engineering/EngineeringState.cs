using System;
using System.Collections.Generic;
using FunProject.Currencies;
using FunProject.GameState;
using FunProject.Items;
using FunProject.Strategic;

namespace FunProject.Engineering;

/// <summary>Campaign-owned manufacturing catalog, queries, and job progress.</summary>
public sealed class EngineeringState
{
  private readonly List<ManufacturingProject> _projects = [];
  private readonly Dictionary<ManufacturingRecipe, ManufacturingProject> _projectsByRecipe = new(ReferenceEqualityComparer.Instance);

  internal EngineeringState(IReadOnlyList<ManufacturingRecipe> initialRecipes)
  {
    Unlock(initialRecipes);
  }

  // Registers unseen recipe references, baking duration and stock policy at first registration.
  // Parse the whole batch before changing the registry, including costs of unseen recipes.
  internal void Unlock(IReadOnlyList<ManufacturingRecipe> recipes)
  {
    ArgumentNullException.ThrowIfNull(recipes);
    foreach (var recipe in recipes)
      ArgumentNullException.ThrowIfNull(recipe);

    var pending = new List<ManufacturingProject>();
    var seen = new SysColGeneric.HashSet<ManufacturingRecipe>(ReferenceEqualityComparer.Instance);
    foreach (var recipe in recipes)
    {
      if (_projectsByRecipe.ContainsKey(recipe) || !seen.Add(recipe)) continue;
      ArgumentNullException.ThrowIfNull(recipe.Item);
      var project = new ManufacturingProject(recipe, recipe.Item, recipe.DurationDays, recipe.Item.UnlimitedStock,
        Currency.CaptureCosts(recipe.Costs));
      pending.Add(project);
    }
    foreach (var project in pending)
    {
      _projectsByRecipe.Add(project.Recipe, project);
      _projects.Add(project);
    }
  }

  public Option<ManufacturingJob> ActiveJob { get; private set; }

  public IReadOnlyList<ManufacturingOption> GetManufacturingOptions(Armory armory,
    IReadOnlyDictionary<Currency, long> balances)
  {
    ArgumentNullException.ThrowIfNull(armory);
    ArgumentNullException.ThrowIfNull(balances);
    return _projects.AsValueEnumerable()
      .Where(project => !project.UnlimitedStock || armory.TryGetItemStock(project.Item).IsNone)
      .Select(project => new ManufacturingOption(project,
        armory.TryGetItemStock(project.Item).Match(stock => stock.Remaining, () => 0),
        Currency.GetShortfalls(project.Costs, balances)))
      .ToImmutableList();
  }

  public Either<ManufacturingStartFailure, ManufacturingProject> ResolveManufacturing(ManufacturingRecipe recipe, Armory armory)
  {
    ArgumentNullException.ThrowIfNull(recipe);
    ArgumentNullException.ThrowIfNull(armory);
    if (!_projectsByRecipe.TryGetValue(recipe, out var project))
      return ManufacturingStartFailure.UnknownRecipe;
    if (ActiveJob.IsSome)
      return ManufacturingStartFailure.Busy;
    if (project.UnlimitedStock && armory.TryGetItemStock(project.Item).IsSome)
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
