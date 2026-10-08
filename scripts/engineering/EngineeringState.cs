using System;
using System.Collections.Generic;
using FunProject.GameState;
using CampaignGameState = FunProject.GameState.GameState;
using FunProject.Strategic;

namespace FunProject.Engineering;

/// <summary>Campaign-owned manufacturing catalog, queries, and job progress.</summary>
public sealed class EngineeringState
{
  private readonly List<ManufacturingRecipe> _projects = [];
  private readonly SysColGeneric.HashSet<ManufacturingRecipe> _registered = new(ReferenceEqualityComparer.Instance);

  internal EngineeringState(IReadOnlyList<ManufacturingRecipe> initialRecipes)
  {
    Unlock(initialRecipes);
  }

  // Register each recipe reference once, preserving authored order.
  internal void Unlock(IReadOnlyList<ManufacturingRecipe> recipes)
  {
    foreach (var recipe in recipes)
      if (_registered.Add(recipe))
        _projects.Add(recipe);
  }

  public Option<ManufacturingJob> ActiveJob { get; private set; }

  public IReadOnlyList<ManufacturingOption> GetManufacturingOptions(CampaignGameState state)
  {
    ArgumentNullException.ThrowIfNull(state);
    var options = new List<ManufacturingOption>();
    foreach (var project in _projects)
    {
      var stock = state.Armory.TryGetItemStock(project.Item);
      if (project.Item.UnlimitedStock && stock.IsSome) continue;
      options.Add(new ManufacturingOption(project,
        stock.Match(entry => entry.Remaining, () => 0)));
    }
    return options.AsReadOnly();
  }

  public Either<ManufacturingStartFailure, ManufacturingRecipe> ResolveManufacturing(ManufacturingRecipe recipe, Armory armory)
  {
    ArgumentNullException.ThrowIfNull(recipe);
    ArgumentNullException.ThrowIfNull(armory);
    if (!_registered.Contains(recipe))
      return ManufacturingStartFailure.UnknownRecipe;
    if (ActiveJob.IsSome)
      return ManufacturingStartFailure.Busy;
    if (recipe.Item.UnlimitedStock && armory.TryGetItemStock(recipe.Item).IsSome)
      return ManufacturingStartFailure.AlreadyAvailable;
    return recipe;
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
