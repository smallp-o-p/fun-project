using FunProject.Engineering;
using FunProject.GameState;
using GdUnit4;
using SysColGeneric = System.Collections.Generic;

[TestSuite]
[RequireGodotRuntime]
public class ProjectCatalogTest
{
  [TestCase(1U, 1457L)]
  [TestCase(uint.MaxValue, 6_184_752_904_817L)]
  public void WholeDaysArePreservedAndScheduledFromSubmissionTime(uint days, long completesAtTick)
  {
    var item = MakeItemData(manufacturingDays: days);
    using var campaign = new GeoscapeFixture(MakeStart(manufacturableItems: [item]));
    Assert.Equal(days, campaign.State.Engineering.GetManufacturingOptions(campaign.State.Armory)[0].Project.DurationDays);

    campaign.AdvanceTicks(17);
    var job = campaign.Session.StartManufacturing(item).RequireRight();
    Assert.Equal(17L, job.StartedAtTick);
    Assert.Equal(completesAtTick, job.CompletesAtTick);
  }

  [TestCase]
  public void NullCatalogEntriesFailCampaignConstruction()
  {
    Assert.Throws<ArgumentNullException>(() => new GameState(MakeStart(
      manufacturableItems: [null!])));
  }

  [TestCase]
  public void NullCatalogArrayFailsCampaignConstruction()
  {
    var start = MakeStart();
    start.ManufacturableItems = null!;
    Assert.Throws<ArgumentNullException>(() => new GameState(start));
  }

  [TestCase]
  public void UnenrolledManufacturingDurationIsIgnored()
  {
    var item = MakeItemData(manufacturingDays: 0);
    var state = new GameState(MakeStart(armory: [item]));
    Assert.True(state.Armory.HasAvailableItem(item));
    Assert.Equal(0, state.Engineering.GetManufacturingOptions(state.Armory).Count);
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
    var secondItem = MakeItemData();
    var state = new GameState(MakeStart(armory: [firstItem],
      manufacturableItems: [secondItem, firstItem]));
    var options = state.Engineering.GetManufacturingOptions(state.Armory);
    Assert.Equal(2, options.Count);
    Assert.Equal(secondItem, options[0].Project.Item);
    Assert.Equal(firstItem, options[1].Project.Item);
    Assert.Equal(1, state.Armory.ItemStock().Count);
    Assert.Equal(1, state.Armory.ItemStock()[0].Remaining);
    Assert.False(state.Armory.HasAvailableItem(secondItem));
    Assert.True(state.Armory.TryGetItemStock(secondItem).IsNone);
  }

  [TestCase]
  public void CatalogCopiesArraysAndBakesExportedValues()
  {
    var item = MakeItemData(manufacturingDays: 2);
    var replacement = MakeItemData();
    var start = MakeStart(manufacturableItems: [item]);
    var state = new GameState(start);

    start.ManufacturableItems[0] = replacement;
    item.ManufacturingDurationDays = 0;
    item.UnlimitedStock = true;

    var options = state.Engineering.GetManufacturingOptions(state.Armory);
    Assert.Equal(item, options[0].Project.Item);
    Assert.Equal(2U, options[0].Project.DurationDays);
    Assert.False(options[0].Project.UnlimitedStock);
    Assert.Throws<NotSupportedException>(() =>
      ((SysColGeneric.IList<ManufacturingOption>)options).Clear());
    var resolved = state.Engineering.ResolveManufacturing(item, state.Armory).RequireRight();
    Assert.Equal(2U, resolved.DurationDays);
    Assert.False(resolved.UnlimitedStock);
    Assert.Equal(ManufacturingStartFailure.UnknownItem,
      state.Engineering.ResolveManufacturing(replacement, state.Armory).RequireLeft());
  }
}
