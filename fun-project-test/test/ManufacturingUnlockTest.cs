using System;
using FunProject.Engineering;
using GdUnit4;

namespace FunProject.Tests;

[TestSuite]
[RequireGodotRuntime]
public class ManufacturingUnlockTest
{
  [TestCase]
  public void UnlockRegistersUnknownItemsOnceInFirstRegistrationOrderWithoutSideEffects()
  {
    var initial = MakeItemData("Initial");
    var second = MakeItemData("Second");
    var first = MakeItemData("First");
    using var campaign = new GeoscapeFixture(MakeStart(
      manufacturableItems: [initial, initial],
      researchProjects: [MakeResearch(manufacturingUnlocks: [MakeItemData("Catalog reward")])]));
    var engineering = campaign.State.Engineering;

    engineering.Unlock([second, first, second]);
    var options = campaign.Session.GetManufacturingOptions();
    Assert.Equal(3, options.Count);
    Assert.Equal(initial, options[0].Project.Item);
    Assert.Equal(second, options[1].Project.Item);
    Assert.Equal(first, options[2].Project.Item);
    var initialProject = options[0].Project;
    var secondProject = options[1].Project;
    var firstProject = options[2].Project;

    engineering.Unlock([first, initial, second]);
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
    var initial = MakeItemData("Initial");
    var item = MakeItemData("Item");
    using var campaign = new GeoscapeFixture(MakeStart(manufacturableItems: [initial]));
    var engineering = campaign.State.Engineering;
    var initialProject = campaign.Session.GetManufacturingOptions()[0].Project;

    Assert.Throws<ArgumentNullException>(() => engineering.Unlock([item, null]));
    Assert.Throws<ArgumentNullException>(() => engineering.Unlock(null));

    var options = campaign.Session.GetManufacturingOptions();
    Assert.Equal(1, options.Count);
    Assert.Equal(initialProject, options[0].Project);
    Assert.Equal(ManufacturingStartFailure.UnknownItem,
      campaign.Session.StartManufacturing(item).RequireLeft());
    Assert.Equal(0, campaign.State.Armory.ItemStock().Count);
    Assert.Equal(0, campaign.Events.Count);
  }

  [TestCase]
  public void RegistrationChangesUnknownToBusyThenExistingSupplyFailure()
  {
    var initial = MakeItemData("Initial");
    var reward = MakeItemData("Reward", unlimited: true);
    using var campaign = new GeoscapeFixture(MakeStart(
      armory: [reward],
      manufacturableItems: [initial],
      researchProjects: [MakeResearch(manufacturingUnlocks: [reward])]));

    Assert.Equal(ManufacturingStartFailure.UnknownItem,
      campaign.Session.StartManufacturing(reward).RequireLeft());
    var active = campaign.Session.StartManufacturing(initial).RequireRight();
    Assert.Equal(ManufacturingStartFailure.UnknownItem,
      campaign.Session.StartManufacturing(reward).RequireLeft());

    campaign.State.Engineering.Unlock([reward]);
    Assert.Equal(ManufacturingStartFailure.Busy,
      campaign.Session.StartManufacturing(reward).RequireLeft());
    Assert.Equal(Some(active), campaign.Session.ActiveManufacturing);

    campaign.AdvanceTicks(1440);
    Assert.Equal(ManufacturingStartFailure.AlreadyAvailable,
      campaign.Session.StartManufacturing(reward).RequireLeft());
    Assert.Equal(1, campaign.Session.GetManufacturingOptions().Count);
  }

  [TestCase(false)]
  [TestCase(true)]
  public void FirstRegistrationCapturesCurrentDurationAndStockPolicy(bool unlimited)
  {
    var item = MakeItemData("Product", unlimited: !unlimited, manufacturingDays: 2);
    using var campaign = new GeoscapeFixture(MakeStart());
    item.ManufacturingDurationDays = 3;
    item.UnlimitedStock = unlimited;

    campaign.State.Engineering.Unlock([item]);
    var project = campaign.Session.GetManufacturingOptions()[0].Project;
    item.ManufacturingDurationDays = 90;
    item.UnlimitedStock = !unlimited;
    campaign.State.Engineering.Unlock([item]);

    var option = campaign.Session.GetManufacturingOptions()[0];
    Assert.Equal(project, option.Project);
    Assert.Equal(3U, option.Project.DurationDays);
    Assert.Equal(unlimited, option.Project.UnlimitedStock);
    var job = campaign.Session.StartManufacturing(item).RequireRight();
    Assert.Equal(4320L, job.CompletesAtTick);
    Assert.Equal(0, campaign.State.Armory.ItemStock().Count);

    campaign.AdvanceTicks(4320);
    Assert.True(campaign.State.Armory.TryWithdrawItem(item).IsSome);
    Assert.Equal(unlimited, campaign.State.Armory.TryWithdrawItem(item).IsSome);
  }
}
