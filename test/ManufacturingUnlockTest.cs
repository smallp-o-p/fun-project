#nullable disable warnings
using System;
using FunProject.Engineering;
using GdUnit4;

namespace FunProject.Tests;

[TestSuite]
[RequireGodotRuntime]
public class ManufacturingUnlockTest
{
  [TestCase]
  public void UnlockRegistersUnknownRecipesOnceInFirstRegistrationOrderWithoutSideEffects()
  {
    var initial = MakeItemData("Initial");
    var initialRecipe = MakeManufacturingRecipe(initial);
    var second = MakeItemData("Second");
    var secondRecipe = MakeManufacturingRecipe(second);
    var first = MakeItemData("First");
    var firstRecipe = MakeManufacturingRecipe(first);
    using var campaign = new GeoscapeFixture(MakeStart(
      manufacturingRecipes: [initialRecipe, initialRecipe],
      researchProjects: [MakeResearch(manufacturingUnlocks: [MakeManufacturingRecipe(MakeItemData("Catalog reward"))])]));
    var engineering = campaign.State.Engineering;

    engineering.Unlock([secondRecipe, firstRecipe, secondRecipe]);
    var options = campaign.Session.GetManufacturingOptions();
    Assert.Equal(3, options.Count);
    Assert.Equal(initial, options[0].Project.Item);
    Assert.Equal(second, options[1].Project.Item);
    Assert.Equal(first, options[2].Project.Item);
    var initialProject = options[0].Project;
    var secondProject = options[1].Project;
    var firstProject = options[2].Project;

    engineering.Unlock([firstRecipe, initialRecipe, secondRecipe]);
    options = campaign.Session.GetManufacturingOptions();
    Assert.Equal(3, options.Count);
    Assert.Equal(initialProject, options[0].Project);
    Assert.Equal(secondProject, options[1].Project);
    Assert.Equal(firstProject, options[2].Project);
    Assert.Equal(0, campaign.State.Armory.ItemStock().Count);
    Assert.Equal(0, campaign.Events.Count);
  }

  [TestCase]
  public void NullUnlockBatchLeavesRegistryUnchanged()
  {
    var currency = MakeCurrency();
    var initial = MakeItemData("Initial");
    var initialRecipe = MakeManufacturingRecipe(initial);
    var item = MakeItemData("Item");
    var itemRecipe = MakeManufacturingRecipe(item);
    using var campaign = new GeoscapeFixture(MakeStart(manufacturingRecipes: [initialRecipe]));
    var engineering = campaign.State.Engineering;
    var initialProject = campaign.Session.GetManufacturingOptions()[0].Project;

    Assert.Throws<ArgumentNullException>(() => engineering.Unlock([itemRecipe, null]));
    Assert.Throws<ArgumentNullException>(() => engineering.Unlock(null));
    var malformed = MakeItemData();
    var malformedRecipe = MakeManufacturingRecipe(malformed, costs: new() { [currency] = -1 });
    Assert.Throws<ArgumentOutOfRangeException>(() => engineering.Unlock([itemRecipe, malformedRecipe]));
    Assert.Equal(ManufacturingStartFailure.UnknownRecipe, campaign.Session.StartManufacturing(malformedRecipe).RequireLeft());

    var options = campaign.Session.GetManufacturingOptions();
    Assert.Equal(1, options.Count);
    Assert.Equal(initialProject, options[0].Project);
    Assert.Equal(ManufacturingStartFailure.UnknownRecipe,
      campaign.Session.StartManufacturing(itemRecipe).RequireLeft());
    Assert.Equal(0, campaign.State.Armory.ItemStock().Count);
    Assert.Equal(0, campaign.Events.Count);
  }

  [TestCase]
  public void RegistrationChangesUnknownToBusyThenExistingSupplyFailure()
  {
    var initial = MakeItemData("Initial");
    var initialRecipe = MakeManufacturingRecipe(initial);
    var reward = MakeItemData("Reward", unlimited: true);
    var rewardRecipe = MakeManufacturingRecipe(reward);
    using var campaign = new GeoscapeFixture(MakeStart(
      armory: [reward],
      manufacturingRecipes: [initialRecipe],
      researchProjects: [MakeResearch(manufacturingUnlocks: [rewardRecipe])]));

    Assert.Equal(ManufacturingStartFailure.UnknownRecipe,
      campaign.Session.StartManufacturing(rewardRecipe).RequireLeft());
    var active = campaign.Session.StartManufacturing(initialRecipe).RequireRight();
    Assert.Equal(ManufacturingStartFailure.UnknownRecipe,
      campaign.Session.StartManufacturing(rewardRecipe).RequireLeft());

    campaign.State.Engineering.Unlock([rewardRecipe]);
    Assert.Equal(ManufacturingStartFailure.Busy,
      campaign.Session.StartManufacturing(rewardRecipe).RequireLeft());
    Assert.Equal(Some(active), campaign.Session.ActiveManufacturing);

    campaign.AdvanceTicks(1440);
    Assert.Equal(ManufacturingStartFailure.AlreadyAvailable,
      campaign.Session.StartManufacturing(rewardRecipe).RequireLeft());
    Assert.Equal(1, campaign.Session.GetManufacturingOptions().Count);
  }

  [TestCase(false)]
  [TestCase(true)]
  public void FirstRegistrationCapturesCurrentDurationAndStockPolicy(bool unlimited)
  {
    var currency = MakeCurrency();
    var item = MakeItemData("Product", unlimited: !unlimited);
    var itemRecipe = MakeManufacturingRecipe(item, days: 2, costs: new() { [currency] = 7 });
    var research = MakeResearch(days: 0, manufacturingUnlocks: [itemRecipe]);
    using var campaign = new GeoscapeFixture(MakeStart(researchProjects: [research], startingCurrencies: new() { [currency] = 7 }));
    itemRecipe.DurationDays = 3;
    item.UnlimitedStock = unlimited;

    campaign.Session.StartResearch(research).RequireRight();
    campaign.AdvanceTicks(1);
    var project = campaign.Session.GetManufacturingOptions()[0].Project;
    itemRecipe.DurationDays = 90;
    item.UnlimitedStock = !unlimited;
    itemRecipe.Costs[currency] = 99;
    campaign.State.Engineering.Unlock([itemRecipe]);

    var option = campaign.Session.GetManufacturingOptions()[0];
    Assert.Equal(project, option.Project);
    Assert.Equal(3U, option.Project.DurationDays);
    Assert.Equal(unlimited, option.Project.UnlimitedStock);
    Assert.Equal(7L, option.Project.Costs[currency]);
    Assert.Equal(7L, campaign.State.GetCurrencyBalance(currency));
    var job = campaign.Session.StartManufacturing(itemRecipe).RequireRight();
    Assert.Equal(4321L, job.CompletesAtTick);
    Assert.Equal(0L, campaign.State.GetCurrencyBalance(currency));
    Assert.Equal(0, campaign.State.Armory.ItemStock().Count);

    campaign.AdvanceTicks(4320);
    Assert.True(campaign.State.Armory.TryWithdrawItem(item).IsSome);
    Assert.Equal(unlimited, campaign.State.Armory.TryWithdrawItem(item).IsSome);
  }
}
