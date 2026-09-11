using System.Threading.Tasks;
using FunProject.Engineering;
using GdUnit4;
using Godot;
using static FunProject.Tests.GeoscapeTestScenes;

[TestSuite]
[RequireGodotRuntime]
public class EngineeringViewTest
{
  [TestCase]
  public async Task LongDescriptionScrollsWhileManufactureAndBackStayReachable()
  {
    await using var cleanup = new DeferredNodeCleanup();
    var item = MakeItemData();
    for (int i = 0; i < 30; i++)
      item.Description += "Engineering describes workshop production and the supply behavior of a utility kit. ";
    using var campaign = new GeoscapeFixture(MakeStart(manufacturableItems: [item]));
    var session = campaign.Session;
    var view = CreateEngineeringView();
    CreateUiViewport(view, new Vector2I(800, 600));
    view.Present(campaign.State, session);
    view.GetNode<VBoxContainer>("%ManufacturableItems").GetChild<Button>(0).EmitSignal(Button.SignalName.Pressed);
    await WaitForLayout(view);

    var visible = new Rect2(0, 0, 800, 600);
    Assert.Equal(visible, ScreenRect(view));
    var action = view.GetNode<Button>("%ManufactureButton");
    var back = view.GetNode<Button>("%BackButton");
    Assert.True(visible.Encloses(ScreenRect(action)));
    Assert.True(visible.Encloses(ScreenRect(back)));
    var scroll = view.GetNode<ScrollContainer>("Content/Margin/Layout/Body/Details/DetailsScroll");
    Assert.True(scroll.ClipContents);
    Assert.True(scroll.GetVScrollBar().MaxValue > scroll.GetVScrollBar().Page);
    var actionRect = ScreenRect(action);
    var backRect = ScreenRect(back);
    scroll.ScrollVertical = 10000;
    await WaitForLayout(view);
    Assert.True(scroll.ScrollVertical > 0);
    Assert.True(ScreenRect(scroll).Encloses(ScreenRect(view.GetNode<Label>("%SupplyEffect"))));
    Assert.Equal(actionRect, ScreenRect(action));
    Assert.Equal(backRect, ScreenRect(back));
    action.EmitSignal(Button.SignalName.Pressed);
    bool backed = false;
    view.BackRequested += () => backed = true;
    back.EmitSignal(Button.SignalName.Pressed);
    Assert.True(session.ActiveManufacturing.Match(job => job.Project.Item == item, () => false));
    Assert.True(backed);
  }

  [TestCase]
  public void EmptyCatalogExplainsNoOptionsWithoutSelectableRows()
  {
    using var campaign = new GeoscapeFixture(MakeStart());
    var view = AddToTree(CreateEngineeringView());
    view.Present(campaign.State, campaign.Session);

    var items = view.GetNode<VBoxContainer>("%ManufacturableItems");
    Assert.Equal(1, items.GetChildCount());
    Assert.Equal("No items available to manufacture.", items.GetChild<Label>(0).Text);
    Assert.Equal("No active manufacturing.", view.GetNode<Label>("%ActiveJob").Text);
    Assert.True(view.GetNode<Button>("%ManufactureButton").Disabled);
  }

  [TestCase]
  public void SelectingRowsDisplaysParsedDurationAndScarceStockInCatalogOrder()
  {
    var first = MakeItemData("Field scanner", manufacturingDays: 1);
    first.Description = "Survey the terrain from a safe distance.";
    var second = MakeItemData("Medkit", manufacturingDays: 2);
    second.Description = "Treat injured soldiers.";
    using var campaign = new GeoscapeFixture(MakeStart(
      armory: [first], manufacturableItems: [first, second]));
    campaign.State.Armory.AddStock(first, 1);
    var session = campaign.Session;
    first.ManufacturingDurationDays = 99;
    first.UnlimitedStock = true;
    var view = AddToTree(CreateEngineeringView());
    view.Present(campaign.State, session);

    var items = view.GetNode<VBoxContainer>("%ManufacturableItems");
    Assert.Equal("Field scanner", items.GetChild<Button>(0).Text);
    Assert.Equal("Medkit", items.GetChild<Button>(1).Text);
    items.GetChild<Button>(0).EmitSignal(Button.SignalName.Pressed);
    Assert.Equal("Field scanner", view.GetNode<Label>("%ItemName").Text);
    Assert.Equal(first.Description, view.GetNode<Label>("%ItemDescription").Text);
    Assert.Equal("Duration: 1d 0h 0m", view.GetNode<Label>("%ManufacturingDuration").Text);
    Assert.Equal("Stock: 2", view.GetNode<Label>("%Stock").Text);
    Assert.Equal("Produces 1 item.", view.GetNode<Label>("%SupplyEffect").Text);
    items.GetChild<Button>(1).EmitSignal(Button.SignalName.Pressed);
    Assert.Equal("Medkit", view.GetNode<Label>("%ItemName").Text);
    Assert.Equal(second.Description, view.GetNode<Label>("%ItemDescription").Text);
    Assert.Equal("Duration: 2d 0h 0m", view.GetNode<Label>("%ManufacturingDuration").Text);
    Assert.Equal("Stock: 0", view.GetNode<Label>("%Stock").Text);
  }

  [TestCase]
  public async Task ActiveJobDisablesExistingAndNewSelectionAndIgnoresProgrammaticSignal()
  {
    await using var cleanup = new DeferredNodeCleanup();
    var first = MakeItemData("Scanner", manufacturingDays: 1);
    var second = MakeItemData("Medkit");
    using var campaign = new GeoscapeFixture(MakeStart(
      manufacturableItems: [first, second]));
    var session = campaign.Session;
    var view = AddToTree(CreateEngineeringView());
    view.Present(campaign.State, session);
    view.GetNode<VBoxContainer>("%ManufacturableItems").GetChild<Button>(0)
      .EmitSignal(Button.SignalName.Pressed);
    Assert.True(session.StartManufacturing(first).IsRight);
    view.Present(campaign.State, session);
    Assert.True(view.GetNode<Button>("%ManufactureButton").Disabled);
    view.GetNode<Button>("%ManufactureButton").EmitSignal(Button.SignalName.Pressed);
    view.GetNode<VBoxContainer>("%ManufacturableItems").GetChild<Button>(1)
      .EmitSignal(Button.SignalName.Pressed);

    Assert.Equal("Medkit", view.GetNode<Label>("%ItemName").Text);
    Assert.True(view.GetNode<Button>("%ManufactureButton").Disabled);
    view.GetNode<Button>("%ManufactureButton").EmitSignal(Button.SignalName.Pressed);
    Assert.True(session.ActiveManufacturing.Match(job => job.Project.Item == first, () => false));
    campaign.AdvanceTicks(1440);
    view.Present(campaign.State, session);
    Assert.False(view.GetNode<Button>("%ManufactureButton").Disabled);
    Assert.Equal("No active manufacturing.", view.GetNode<Label>("%ActiveJob").Text);
  }

  [TestCase]
  public async Task ActiveRemainingTimeUsesSuppliedTickAndClampsElapsedCompletion()
  {
    await using var cleanup = new DeferredNodeCleanup();
    var item = MakeItemData("Field scanner", manufacturingDays: 1);
    using var campaign = new GeoscapeFixture(MakeStart(manufacturableItems: [item]));
    var session = campaign.Session;
    Assert.True(session.StartManufacturing(item).IsRight);
    campaign.AdvanceTicks(2);
    var view = AddToTree(CreateEngineeringView());
    view.Present(campaign.State, session);
    Assert.Equal("Manufacturing: Field scanner — 23h 58m remaining", view.GetNode<Label>("%ActiveJob").Text);
    view.Present(session.GetManufacturingOptions(), session.ActiveManufacturing, 1443);
    Assert.Equal("Manufacturing: Field scanner — 0m remaining", view.GetNode<Label>("%ActiveJob").Text);
    Assert.Equal(2L, session.Tick);
    Assert.True(session.ActiveManufacturing.IsSome);
  }

  [TestCase]
  public async Task PresentPreservesItemIdentityAcrossReorderingAndRefreshesParsedEntry()
  {
    await using var cleanup = new DeferredNodeCleanup();
    var first = MakeItemData("First");
    var second = MakeItemData("Second", manufacturingDays: 1);
    using var campaign = new GeoscapeFixture(MakeStart(
      manufacturableItems: [first, second]));
    var session = campaign.Session;
    var view = AddToTree(CreateEngineeringView());
    view.Present(session.GetManufacturingOptions(), session.ActiveManufacturing, session.Tick);
    view.GetNode<VBoxContainer>("%ManufacturableItems").GetChild<Button>(1)
      .EmitSignal(Button.SignalName.Pressed);
    second.ManufacturingDurationDays = 2;
    using var refreshedCampaign = new GeoscapeFixture(MakeStart(
      armory: [second], manufacturableItems: [second, first]));
    refreshedCampaign.State.Armory.AddStock(second, 2);
    var refreshed = refreshedCampaign.Session;
    view.Present(refreshed.GetManufacturingOptions(), refreshed.ActiveManufacturing, refreshed.Tick);

    Assert.Equal("Second", view.GetNode<Label>("%ItemName").Text);
    Assert.Equal("Duration: 2d 0h 0m", view.GetNode<Label>("%ManufacturingDuration").Text);
    Assert.Equal("Stock: 3", view.GetNode<Label>("%Stock").Text);
    Assert.False(view.GetNode<Button>("%ManufactureButton").Disabled);
    view.Present(refreshedCampaign.State, refreshed); // activation binding
    view.GetNode<Button>("%ManufactureButton").EmitSignal(Button.SignalName.Pressed);
    Assert.True(refreshed.ActiveManufacturing.Match(job => job.Project.Item == second, () => false));
  }

  [TestCase]
  public async Task PresentClearsStaleSelectionEvenWhenReplacementHasSameName()
  {
    await using var cleanup = new DeferredNodeCleanup();
    var item = MakeItemData("Scanner");
    using var campaign = new GeoscapeFixture(MakeStart(manufacturableItems: [item]));
    var session = campaign.Session;
    var view = AddToTree(CreateEngineeringView());
    view.Present(session.GetManufacturingOptions(), session.ActiveManufacturing, session.Tick);
    view.GetNode<VBoxContainer>("%ManufacturableItems").GetChild<Button>(0)
      .EmitSignal(Button.SignalName.Pressed);
    var replacement = MakeItemData("Scanner");
    using var refreshedCampaign = new GeoscapeFixture(MakeStart(
      manufacturableItems: [replacement]));
    var refreshed = refreshedCampaign.Session;
    view.Present(refreshed.GetManufacturingOptions(), refreshed.ActiveManufacturing, refreshed.Tick);

    Assert.True(view.GetNode<Button>("%ManufactureButton").Disabled);
    Assert.Equal("Select an item to manufacture.", view.GetNode<Label>("%ItemName").Text);
    Assert.Equal("", view.GetNode<Label>("%ItemDescription").Text);
    Assert.Equal("", view.GetNode<Label>("%ManufacturingDuration").Text);
    Assert.Equal("", view.GetNode<Label>("%Stock").Text);
    Assert.Equal("", view.GetNode<Label>("%SupplyEffect").Text);
    view.GetNode<Button>("%ManufactureButton").EmitSignal(Button.SignalName.Pressed); // disabled: no-op
  }

  [TestCase]
  public async Task ActivatedUnlimitedItemDisappearsAndClearsSelectionWhileOtherItemsRemain()
  {
    await using var cleanup = new DeferredNodeCleanup();
    var supply = MakeItemData("Supply", unlimited: true, manufacturingDays: 1);
    var scarce = MakeItemData("Medkit");
    using var campaign = new GeoscapeFixture(MakeStart(
      manufacturableItems: [supply, scarce]));
    var session = campaign.Session;
    var view = AddToTree(CreateEngineeringView());
    view.Present(campaign.State, session);
    var items = view.GetNode<VBoxContainer>("%ManufacturableItems");
    Assert.Equal(2, items.GetChildCount());
    items.GetChild<Button>(0).EmitSignal(Button.SignalName.Pressed);
    Assert.Equal("Stock: Not established", view.GetNode<Label>("%Stock").Text);
    Assert.Equal("Establishes unlimited supply.", view.GetNode<Label>("%SupplyEffect").Text);
    Assert.False(view.GetNode<Button>("%ManufactureButton").Disabled);
    session.StartManufacturing(supply).RequireRight();
    campaign.AdvanceTicks(1440);
    view.Present(campaign.State, session);

    Assert.Equal(1, items.GetChildCount());
    Assert.Equal("Medkit", items.GetChild<Button>(0).Text);
    Assert.Equal("Select an item to manufacture.", view.GetNode<Label>("%ItemName").Text);
    Assert.Equal("", view.GetNode<Label>("%Stock").Text);
    Assert.Equal("", view.GetNode<Label>("%SupplyEffect").Text);
    Assert.True(view.GetNode<Button>("%ManufactureButton").Disabled);
    view.GetNode<Button>("%ManufactureButton").EmitSignal(Button.SignalName.Pressed); // disabled: no-op
    items.GetChild<Button>(0).EmitSignal(Button.SignalName.Pressed);
    Assert.False(view.GetNode<Button>("%ManufactureButton").Disabled);
  }

  [TestCase]
  public async Task RepeatedPresentReplacesRowsAndStartsOneJobPerRequest()
  {
    await using var cleanup = new DeferredNodeCleanup();
    var item = MakeItemData();
    using var campaign = new GeoscapeFixture(MakeStart(manufacturableItems: [item]));
    var session = campaign.Session;
    var view = AddToTree(CreateEngineeringView());
    bool backed = false;
    view.BackRequested += () => backed = true;
    view.Present(campaign.State, session);
    var items = view.GetNode<VBoxContainer>("%ManufacturableItems");
    var oldRow = items.GetChild<Button>(0);
    for (int i = 0; i < 3; i++)
      view.Present(campaign.State, session);

    Assert.True(oldRow.GetParent() is null && oldRow.IsQueuedForDeletion());
    Assert.Equal(1, items.GetChildCount());
    items.GetChild<Button>(0).EmitSignal(Button.SignalName.Pressed);
    Assert.False(view.GetNode<Button>("%ManufactureButton").Disabled);
    view.GetNode<Button>("%ManufactureButton").EmitSignal(Button.SignalName.Pressed);
    Assert.True(session.ActiveManufacturing.Match(job => job.Project.Item == item, () => false));
    Assert.True(view.GetNode<Button>("%ManufactureButton").Disabled);
    Assert.Equal("", view.GetNode<Label>("%Status").Text); // success shows no failure text
    view.GetNode<Button>("%BackButton").EmitSignal(Button.SignalName.Pressed);
    Assert.True(backed);
    Assert.True(view.IsInsideTree() && !view.IsQueuedForDeletion()); // Back never frees the view itself
  }

  [TestCase(ManufacturingStartFailure.UnknownItem, "This item is not available for manufacturing.")]
  [TestCase(ManufacturingStartFailure.Busy, "Another manufacturing project is already in progress.")]
  [TestCase(ManufacturingStartFailure.AlreadyAvailable, "Unlimited supply for this item is already established.")]
  public void FailureExplainsRejectionAndNextPresentClearsIt(ManufacturingStartFailure failure, string message)
  {
    var view = AddToTree(CreateEngineeringView());
    view.ShowFailure(failure);
    Assert.Equal(message, view.GetNode<Label>("%Status").Text);
    using var campaign = new GeoscapeFixture();
    view.Present(campaign.State, campaign.Session);
    Assert.Equal("", view.GetNode<Label>("%Status").Text);
  }
}
