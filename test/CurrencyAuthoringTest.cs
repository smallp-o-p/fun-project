using System;
using FunProject.Currencies;
using FunProject.GameState;
using FunProject.Engineering;
using FunProject.Items;
using FunProject.Research;
using GdUnit4;
using Godot;

namespace FunProject.Tests;

[TestSuite]
[RequireGodotRuntime]
public class CurrencyAuthoringTest
{
  [TestCase]
  public void OwningResourceFilesRetainCurrencyCosts()
  {
    string prefix = $"user://currency-authoring-{Guid.NewGuid():N}";
    string currencyPath = prefix + "-currency.tres";
    string campaignPath = prefix + "-campaign.tres";
    string itemPath = prefix + "-item.tres";
    string recipePath = prefix + "-recipe.tres";
    string researchPath = prefix + "-research.tres";
    using var authoredCurrency = MakeCurrency("Debug currency");
    try
    {
      Assert.Equal(Error.Ok, ResourceSaver.Save(authoredCurrency, currencyPath));
      using var currency = ResourceLoader.Load<Currency>(currencyPath);
      Assert.Equal(currencyPath, currency.ResourcePath);
      using var authoredItem = MakeItemData();
      Assert.Equal(Error.Ok, ResourceSaver.Save(authoredItem, itemPath));
      using var item = ResourceLoader.Load<EquippableItemData>(itemPath);
      using var authoredRecipe = MakeManufacturingRecipe(item, days: 2, costs: new() { [currency] = long.MaxValue - 1 });
      Assert.Equal(Error.Ok, ResourceSaver.Save(authoredRecipe, recipePath));
      using var recipe = ResourceLoader.Load<ManufacturingRecipe>(recipePath);
      using var start = MakeStart(manufacturingRecipes: [recipe], startingCurrencies: new() { [currency] = long.MaxValue });
      using var research = MakeResearch(manufacturingUnlocks: [recipe], costs: new() { [currency] = 11 });
      Assert.Equal(Error.Ok, ResourceSaver.Save(start, campaignPath));
      Assert.Equal(Error.Ok, ResourceSaver.Save(research, researchPath));
      using var loadedStart = ResourceLoader.Load<CampaignStartData>(campaignPath, cacheMode: ResourceLoader.CacheMode.Ignore);
      using var loadedResearch = ResourceLoader.Load<ResearchProject>(researchPath, cacheMode: ResourceLoader.CacheMode.Ignore);
      var loadedRecipe = loadedStart.ManufacturingRecipes[0];
      Assert.True(ReferenceEquals(loadedRecipe, loadedResearch.ManufacturingUnlocks[0]));
      Assert.Equal(itemPath, loadedRecipe.Item.ResourcePath);
      Assert.Equal(recipePath, loadedRecipe.ResourcePath);
      Assert.Equal(2U, loadedRecipe.DurationDays);
      Currency loadedCurrency = loadedStart.StartingCurrencies.Keys.AsValueEnumerable().Single();
      Assert.Equal(currencyPath, loadedCurrency.ResourcePath);
      Assert.True(ReferenceEquals(loadedCurrency, loadedRecipe.Costs.Keys.AsValueEnumerable().Single()));
      Assert.True(ReferenceEquals(loadedCurrency, loadedResearch.Costs.Keys.AsValueEnumerable().Single()));
      Assert.Equal(long.MaxValue, loadedStart.StartingCurrencies[loadedCurrency]);
      Assert.Equal(long.MaxValue - 1, loadedRecipe.Costs[loadedCurrency]);
      Assert.Equal(11L, loadedResearch.Costs[loadedCurrency]);
    }
    finally
    {
      foreach (string path in new[] { researchPath, campaignPath, recipePath, itemPath, currencyPath })
        DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(path));
    }
  }
}
