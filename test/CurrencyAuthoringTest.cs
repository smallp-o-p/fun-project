using System;
using FunProject.Currencies;
using FunProject.GameState;
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
    string researchPath = prefix + "-research.tres";
    using var authoredCurrency = MakeCurrency("Debug currency");
    try
    {
      Assert.Equal(Error.Ok, ResourceSaver.Save(authoredCurrency, currencyPath));
      using var currency = ResourceLoader.Load<Currency>(currencyPath);
      Assert.Equal(currencyPath, currency.ResourcePath);
      using var start = MakeStart(startingCurrencies: new() { [currency] = long.MaxValue });
      using var item = MakeItemData(manufacturingCosts: new() { [currency] = long.MaxValue - 1 });
      using var research = MakeResearch(costs: new() { [currency] = 11 });
      Assert.Equal(Error.Ok, ResourceSaver.Save(start, campaignPath));
      Assert.Equal(Error.Ok, ResourceSaver.Save(item, itemPath));
      Assert.Equal(Error.Ok, ResourceSaver.Save(research, researchPath));
      using var loadedStart = ResourceLoader.Load<CampaignStartData>(campaignPath, cacheMode: ResourceLoader.CacheMode.Ignore);
      using var loadedItem = ResourceLoader.Load<EquippableItemData>(itemPath, cacheMode: ResourceLoader.CacheMode.Ignore);
      using var loadedResearch = ResourceLoader.Load<ResearchProject>(researchPath, cacheMode: ResourceLoader.CacheMode.Ignore);
      Currency loadedCurrency = loadedStart.StartingCurrencies.Keys.AsValueEnumerable().Single();
      Assert.Equal(currencyPath, loadedCurrency.ResourcePath);
      Assert.True(ReferenceEquals(loadedCurrency, loadedItem.ManufacturingCosts.Keys.AsValueEnumerable().Single()));
      Assert.True(ReferenceEquals(loadedCurrency, loadedResearch.Costs.Keys.AsValueEnumerable().Single()));
      Assert.Equal(long.MaxValue, loadedStart.StartingCurrencies[loadedCurrency]);
      Assert.Equal(long.MaxValue - 1, loadedItem.ManufacturingCosts[loadedCurrency]);
      Assert.Equal(11L, loadedResearch.Costs[loadedCurrency]);
    }
    finally
    {
      foreach (string path in new[] { researchPath, itemPath, campaignPath, currencyPath })
        DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(path));
    }
  }
}
