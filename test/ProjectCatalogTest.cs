using CampaignGameState = global::FunProject.GameState.GameState;
using System;
using FunProject.Engineering;
using FunProject.GameState;
using GdUnit4;

namespace FunProject.Tests;

[TestSuite]
[RequireGodotRuntime]
public class ProjectCatalogTest
{
  [TestCase(1U, 1457L)]
  [TestCase(uint.MaxValue, 6_184_752_904_817L)]
  public static void WholeDaysArePreservedAndScheduledFromSubmissionTime(uint days, long completesAtTick)
  {
    var item = MakeItemData();
    var itemRecipe = MakeManufacturingRecipe(item, days: days);
    using var campaign = new GeoscapeFixture(MakeStart(manufacturingRecipes: [itemRecipe]));
    Assert.Equal(days, campaign.State.Engineering.GetManufacturingOptions(campaign.State)[0].Project.DurationDays);

    campaign.AdvanceTicks(17);
    var job = campaign.Session.StartManufacturing(itemRecipe).RequireRight();
    Assert.Equal(17L, job.StartedAtTick);
    Assert.Equal(completesAtTick, job.CompletesAtTick);
  }

  [TestCase]
  public static void CatalogPreservesOrderAndIdentityWithoutGrantingStock()
  {
    var item = MakeItemData();
    var firstRecipe = MakeManufacturingRecipe(item);
    var secondRecipe = MakeManufacturingRecipe(item, days: 2);
    var state = new CampaignGameState(MakeStart(armory: [item], manufacturingRecipes: [secondRecipe, firstRecipe]));
    var options = state.Engineering.GetManufacturingOptions(state);
    Assert.Equal(2, options.Count);
    Assert.Equal(secondRecipe, options[0].Project);
    Assert.Equal(firstRecipe, options[1].Project);
    Assert.Equal(2U, options[0].Project.DurationDays);
    Assert.Equal(1U, options[1].Project.DurationDays);
    Assert.Equal(item, options[0].Project.Item);
    Assert.Equal(item, options[1].Project.Item);
    Assert.Equal(1, state.Armory.ItemStock().Count);
    Assert.Equal(1, state.Armory.ItemStock()[0].Remaining);
  }

  [TestCase]
  public static void CatalogCopiesMembershipAndReadsRecipeValues()
  {
    var item = MakeItemData();
    var itemRecipe = MakeManufacturingRecipe(item, days: 2);
    var replacement = MakeItemData();
    var replacementRecipe = MakeManufacturingRecipe(replacement);
    var start = MakeStart(manufacturingRecipes: [itemRecipe]);
    var state = new CampaignGameState(start);

    start.ManufacturingRecipes[0] = replacementRecipe;
    itemRecipe.DurationDays = 3;
    itemRecipe.Item = replacement;
    replacement.UnlimitedStock = true;

    var options = state.Engineering.GetManufacturingOptions(state);
    Assert.Equal(replacement, options[0].Project.Item);
    Assert.Equal(3U, options[0].Project.DurationDays);
    Assert.True(options[0].Project.Item.UnlimitedStock);
    Assert.Throws<NotSupportedException>(() =>
      ((SysColGeneric.IList<ManufacturingOption>)options).Clear());
    var resolved = state.Engineering.ResolveManufacturing(itemRecipe, state.Armory).RequireRight();
    Assert.Equal(itemRecipe, resolved);
    Assert.Equal(ManufacturingStartFailure.UnknownRecipe,
      state.Engineering.ResolveManufacturing(replacementRecipe, state.Armory).RequireLeft());
  }
}
