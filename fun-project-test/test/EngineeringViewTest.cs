using System.Threading.Tasks;
using FunProject.Engineering;
using FunProject.Items;
using GdUnit4;
using Godot;
using static FunProject.Tests.GeoscapeUiTestFactory;

[TestSuite]
[RequireGodotRuntime]
public class EngineeringViewTest
{
  [TestCase]
  public async Task LongDescriptionScrollsWhileManufactureAndBackStayReachable()
  {
    var item = MakeItemData();
    for (int i = 0; i < 30; i++)
      item.Description += "Engineering describes workshop production and the supply behavior of a utility kit. ";
    using var campaign = new GeoscapeFixture(MakeStart(manufacturableItems: [item]));
    var session = campaign.Session;
    var view = CreateEngineeringView();
    CreateUiViewport(view, new Vector2I(800, 600));
    view.Present(session.GetManufacturingOptions(), None, 0);
    view.GetNode<VBoxContainer>("%ManufacturableItems").GetChild<Button>(0).EmitSignal(Button.SignalName.Pressed);
    await WaitForLayout(view);

    var visible = new Rect2(0, 0, 800, 600);
    Assert.Equal(visible, ScreenRect(view));
    var action = view.GetNode<Button>("%ManufactureButton");
    var back = view.GetNode<Button>("%BackButton");
    Assert.True(visible.Encloses(ScreenRect(action)));
    Assert.True(visible.Encloses(ScreenRect(back)));
    var scroll = view.GetNode<ScrollContainer>("Margin/Layout/Body/Details/DetailsScroll");
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
    Option<EquippableItemData> requested = None;
    view.ManufactureRequested += data => requested = Some(data);
    bool closed = false;
    view.ArmClose(() => closed = true);
    action.EmitSignal(Button.SignalName.Pressed);
    back.EmitSignal(Button.SignalName.Pressed);
    Assert.True(requested.Match(data => data == item, () => false));
    Assert.True(closed);
  }

  [TestCase]
  public void EmptyCatalogExplainsNoOptionsWithoutSelectableRows()
  {
    using var campaign = new GeoscapeFixture(MakeStart());
    var session = campaign.Session;
    var view = AddToTree(CreateEngineeringView());
    view.Present(session.GetManufacturingOptions(), session.ActiveManufacturing, session.Tick);

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
    view.Present(session.GetManufacturingOptions(), session.ActiveManufacturing, session.Tick);

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
  public void ActiveJobDisablesExistingAndNewSelectionAndIgnoresProgrammaticSignal()
  {
    var first = MakeItemData("Scanner", manufacturingDays: 1);
    var second = MakeItemData("Medkit");
    using var campaign = new GeoscapeFixture(MakeStart(
      manufacturableItems: [first, second]));
    var session = campaign.Session;
    var view = AddToTree(CreateEngineeringView());
    int requests = 0;
    view.ManufactureRequested += _ => requests++;
    view.Present(session.GetManufacturingOptions(), session.ActiveManufacturing, session.Tick);
    view.GetNode<VBoxContainer>("%ManufacturableItems").GetChild<Button>(0)
      .EmitSignal(Button.SignalName.Pressed);
    Assert.True(session.StartManufacturing(first).IsRight);
    view.Present(session.GetManufacturingOptions(), session.ActiveManufacturing, session.Tick);
    Assert.True(view.GetNode<Button>("%ManufactureButton").Disabled);
    view.GetNode<Button>("%ManufactureButton").EmitSignal(Button.SignalName.Pressed);
    view.GetNode<VBoxContainer>("%ManufacturableItems").GetChild<Button>(1)
      .EmitSignal(Button.SignalName.Pressed);

    Assert.Equal("Medkit", view.GetNode<Label>("%ItemName").Text);
    Assert.True(view.GetNode<Button>("%ManufactureButton").Disabled);
    view.GetNode<Button>("%ManufactureButton").EmitSignal(Button.SignalName.Pressed);
    Assert.Equal(0, requests);
    campaign.AdvanceTicks(1440);
    view.Present(session.GetManufacturingOptions(), session.ActiveManufacturing, session.Tick);
    Assert.False(view.GetNode<Button>("%ManufactureButton").Disabled);
    Assert.Equal("No active manufacturing.", view.GetNode<Label>("%ActiveJob").Text);
  }

  [TestCase]
  public void ActiveRemainingTimeUsesSuppliedTickAndClampsElapsedCompletion()
  {
    var item = MakeItemData("Field scanner", manufacturingDays: 1);
    using var campaign = new GeoscapeFixture(MakeStart(manufacturableItems: [item]));
    var session = campaign.Session;
    Assert.True(session.StartManufacturing(item).IsRight);
    campaign.AdvanceTicks(2);
    var view = AddToTree(CreateEngineeringView());
    view.Present(session.GetManufacturingOptions(), session.ActiveManufacturing, session.Tick);
    Assert.Equal("Manufacturing: Field scanner — 23h 58m remaining", view.GetNode<Label>("%ActiveJob").Text);
    view.Present(session.GetManufacturingOptions(), session.ActiveManufacturing, 1443);
    Assert.Equal("Manufacturing: Field scanner — 0m remaining", view.GetNode<Label>("%ActiveJob").Text);
    Assert.Equal(2L, session.Tick);
    Assert.True(session.ActiveManufacturing.IsSome);
  }

  [TestCase]
  public void PresentPreservesItemIdentityAcrossReorderingAndRefreshesParsedEntry()
  {
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
    Option<EquippableItemData> requested = None;
    view.ManufactureRequested += data => requested = Some(data);
    view.GetNode<Button>("%ManufactureButton").EmitSignal(Button.SignalName.Pressed);
    Assert.True(requested.Match(data => data == second, () => false));
  }

  [TestCase]
  public void PresentClearsStaleSelectionEvenWhenReplacementHasSameName()
  {
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
    int requests = 0;
    view.ManufactureRequested += _ => requests++;
    view.GetNode<Button>("%ManufactureButton").EmitSignal(Button.SignalName.Pressed);
    Assert.Equal(0, requests);
  }

  [TestCase]
  public void ActivatedUnlimitedItemDisappearsAndClearsSelectionWhileOtherItemsRemain()
  {
    var supply = MakeItemData("Supply", unlimited: true, manufacturingDays: 1);
    var scarce = MakeItemData("Medkit");
    using var campaign = new GeoscapeFixture(MakeStart(
      manufacturableItems: [supply, scarce]));
    var session = campaign.Session;
    var view = AddToTree(CreateEngineeringView());
    int requests = 0;
    view.ManufactureRequested += _ => requests++;
    view.Present(session.GetManufacturingOptions(), session.ActiveManufacturing, session.Tick);
    var items = view.GetNode<VBoxContainer>("%ManufacturableItems");
    Assert.Equal(2, items.GetChildCount());
    items.GetChild<Button>(0).EmitSignal(Button.SignalName.Pressed);
    Assert.Equal("Stock: Not established", view.GetNode<Label>("%Stock").Text);
    Assert.Equal("Establishes unlimited supply.", view.GetNode<Label>("%SupplyEffect").Text);
    Assert.False(view.GetNode<Button>("%ManufactureButton").Disabled);
    session.StartManufacturing(supply).RequireRight();
    campaign.AdvanceTicks(1440);
    view.Present(session.GetManufacturingOptions(), session.ActiveManufacturing, session.Tick);

    Assert.Equal(1, items.GetChildCount());
    Assert.Equal("Medkit", items.GetChild<Button>(0).Text);
    Assert.Equal("Select an item to manufacture.", view.GetNode<Label>("%ItemName").Text);
    Assert.Equal("", view.GetNode<Label>("%Stock").Text);
    Assert.Equal("", view.GetNode<Label>("%SupplyEffect").Text);
    Assert.True(view.GetNode<Button>("%ManufactureButton").Disabled);
    view.GetNode<Button>("%ManufactureButton").EmitSignal(Button.SignalName.Pressed);
    Assert.Equal(0, requests);
    items.GetChild<Button>(0).EmitSignal(Button.SignalName.Pressed);
    Assert.False(view.GetNode<Button>("%ManufactureButton").Disabled);
  }

  [TestCase]
  public void RepeatedPresentReplacesRowsAndPreservesRequestWiring()
  {
    var item = MakeItemData();
    using var campaign = new GeoscapeFixture(MakeStart(manufacturableItems: [item]));
    var session = campaign.Session;
    var view = AddToTree(CreateEngineeringView());
    int requests = 0;
    int previousCloses = 0;
    int closes = 0;
    Option<EquippableItemData> requested = None;
    view.ManufactureRequested += data => { requests++; requested = Some(data); };
    view.ArmClose(() => previousCloses++);
    view.ArmClose(() => closes++);
    view.Present(session.GetManufacturingOptions(), None, session.Tick);
    var items = view.GetNode<VBoxContainer>("%ManufacturableItems");
    var oldRow = items.GetChild<Button>(0);
    for (int i = 0; i < 3; i++)
      view.Present(session.GetManufacturingOptions(), None, session.Tick);

    Assert.True(oldRow.GetParent() is null && oldRow.IsQueuedForDeletion());
    Assert.Equal(1, items.GetChildCount());
    items.GetChild<Button>(0).EmitSignal(Button.SignalName.Pressed);
    Assert.False(view.GetNode<Button>("%ManufactureButton").Disabled);
    view.GetNode<Button>("%ManufactureButton").EmitSignal(Button.SignalName.Pressed);
    view.GetNode<Button>("%BackButton").EmitSignal(Button.SignalName.Pressed);
    Assert.Equal(Some(item), requested);
    Assert.Equal(1, requests);
    Assert.True(session.ActiveManufacturing.IsNone);
    Assert.Equal(0, previousCloses);
    Assert.Equal(1, closes);
    Assert.True(view.IsInsideTree() && !view.IsQueuedForDeletion());
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
    var session = campaign.Session;
    view.Present(session.GetManufacturingOptions(), session.ActiveManufacturing, session.Tick);
    Assert.Equal("", view.GetNode<Label>("%Status").Text);
  }
}
