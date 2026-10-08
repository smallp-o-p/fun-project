using System;
using FunProject.Engineering;
using FunProject.GameState;
using GdUnit4;

[TestSuite]
[RequireGodotRuntime]
public class ProjectCatalogTest
{
  [TestCase(1U, 1457L)]
  [TestCase(uint.MaxValue, 6_184_752_904_817L)]
  public void WholeDaysArePreservedAndScheduledFromSubmissionTime(uint days, long completesAtTick)
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
  public void NullCatalogAuthoringFailsCampaignConstruction()
  {
    Assert.Throws<ArgumentNullException>(() => new GameState(MakeStart(
      manufacturingRecipes: [null!])));

    var start = MakeStart();
    start.ManufacturingRecipes = null!;
    Assert.Throws<ArgumentNullException>(() => new GameState(start));
  }

  [TestCase]
  public void UnenrolledManufacturingDurationIsIgnored()
  {
    var item = MakeItemData();
    var itemRecipe = MakeManufacturingRecipe(item, days: 0);
    var state = new GameState(MakeStart(armory: [item]));
    Assert.True(state.Armory.HasAvailableItem(item));
    Assert.Equal(0, state.Engineering.GetManufacturingOptions(state).Count);
  }

  [TestCase]
  public void EmptyCatalogRemainsCompatible()
  {
    using var campaign = new GeoscapeFixture(MakeStart());
    Assert.Equal(0, campaign.Session.GetManufacturingOptions().Count);
    Assert.True(campaign.Session.ActiveManufacturing.IsNone);
  }

  [TestCase]
  public void CatalogPreservesOrderAndIdentityWithoutGrantingStock()
  {
    var firstItem = MakeItemData();
    var firstItemRecipe = MakeManufacturingRecipe(firstItem);
    var secondItem = firstItem;
    var secondItemRecipe = MakeManufacturingRecipe(secondItem, days: 2);
    var state = new GameState(MakeStart(armory: [firstItem],
      manufacturingRecipes: [secondItemRecipe, firstItemRecipe]));
    var options = state.Engineering.GetManufacturingOptions(state);
    Assert.Equal(2, options.Count);
    Assert.Equal(secondItemRecipe, options[0].Project.Recipe);
    Assert.Equal(firstItemRecipe, options[1].Project.Recipe);
    Assert.Equal(2U, options[0].Project.DurationDays);
    Assert.Equal(1U, options[1].Project.DurationDays);
    Assert.Equal(secondItem, options[0].Project.Item);
    Assert.Equal(firstItem, options[1].Project.Item);
    Assert.Equal(1, state.Armory.ItemStock().Count);
    Assert.Equal(1, state.Armory.ItemStock()[0].Remaining);
    Assert.True(state.Armory.HasAvailableItem(secondItem));
  }

  [TestCase]
  public void CatalogCopiesArraysAndBakesExportedValues()
  {
    var item = MakeItemData();
    var itemRecipe = MakeManufacturingRecipe(item, days: 2);
    var replacement = MakeItemData();
    var replacementRecipe = MakeManufacturingRecipe(replacement);
    var start = MakeStart(manufacturingRecipes: [itemRecipe]);
    var state = new GameState(start);

    start.ManufacturingRecipes[0] = replacementRecipe;
    itemRecipe.DurationDays = 0;
    itemRecipe.Item = replacement;
    item.UnlimitedStock = true;

    var options = state.Engineering.GetManufacturingOptions(state);
    Assert.Equal(item, options[0].Project.Item);
    Assert.Equal(2U, options[0].Project.DurationDays);
    Assert.False(options[0].Project.UnlimitedStock);
    Assert.Throws<NotSupportedException>(() =>
      ((SysColGeneric.IList<ManufacturingOption>)options).Clear());
    var resolved = state.Engineering.ResolveManufacturing(itemRecipe, state.Armory).RequireRight();
    Assert.Equal(2U, resolved.DurationDays);
    Assert.False(resolved.UnlimitedStock);
    Assert.Equal(ManufacturingStartFailure.UnknownRecipe,
      state.Engineering.ResolveManufacturing(replacementRecipe, state.Armory).RequireLeft());
  }
}
