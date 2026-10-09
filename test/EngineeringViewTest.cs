using System.Threading.Tasks;
using FunProject.Engineering;
using GdUnit4;
using Godot;
using static FunProject.Tests.GeoscapeTestScenes;

namespace FunProject.Tests;

[TestSuite]
[RequireGodotRuntime]
public class EngineeringViewTest
{
  [TestCase(800, 600)]
  [TestCase(1280, 720)]
  [TestCase(1920, 1080)]
  public static async Task WorkshopCatalogAndInspectorFitWithoutHidingActions(int width, int height)
  {
    await using var cleanup = new DeferredNodeCleanup();
    using var campaign = new GeoscapeFixture(MakeStart(manufacturingRecipes: [MakeManufacturingRecipe(MakeItemData())]));
    var view = CreateEngineeringView();
    CreateUiViewport(view, new Vector2I(width, height));
    view.Present(campaign.State, campaign.Session);
    var item = view.GetNode<VBoxContainer>("%ManufacturableItems").GetChild<Button>(0);
    item.EmitSignal(Button.SignalName.Pressed);
    await WaitForLayout(view);
    var visible = new Rect2(0, 0, width, height);
    var staff = view.GetNode<Control>("%StaffPresentation");
    var catalog = view.GetNode<Control>("%Catalog");
    var details = view.GetNode<Control>("%Details");
    Assert.True(visible.Encloses(ScreenRect(staff)));
    Assert.True(ScreenRect(staff).End.X <= ScreenRect(catalog).Position.X);
    Assert.True(ScreenRect(catalog).End.X <= ScreenRect(details).Position.X);
    Assert.True(visible.Encloses(ScreenRect(view.GetNode<Button>("%ManufactureButton"))));
    Assert.True(visible.Encloses(ScreenRect(view.GetNode<Button>("%BackButton"))));
    Assert.True(item.ButtonPressed);
    view.Present(campaign.State, campaign.Session);
    Assert.True(view.GetNode<VBoxContainer>("%ManufacturableItems").GetChild<Button>(0).ButtonPressed);
  }

  [TestCase(800, 600)]
  [TestCase(1600, 900)]
  public static async Task LongDescriptionScrollsWhileManufactureAndBackStayReachable(int width, int height)
  {
    await using var cleanup = new DeferredNodeCleanup();
    var currency = MakeCurrency("A currency with a deliberately long descriptive name for campaign production budgets");
    var item = MakeItemData();
    var itemRecipe = MakeManufacturingRecipe(item, costs: new() { [currency] = ulong.MaxValue });
    for (int i = 0; i < 30; i++)
      item.Description += "Engineering describes workshop production and the supply behavior of a utility kit. ";
    using var campaign = new GeoscapeFixture(MakeStart(manufacturingRecipes: [itemRecipe], startingCurrencies: new() { [currency] = ulong.MaxValue }));
    var session = campaign.Session;
    var view = CreateEngineeringView();
    CreateUiViewport(view, new Vector2I(width, height));
    view.Present(campaign.State, session);
    view.GetNode<VBoxContainer>("%ManufacturableItems").GetChild<Button>(0).EmitSignal(Button.SignalName.Pressed);
    await WaitForLayout(view);

    var visible = new Rect2(0, 0, width, height);
    Assert.Equal(visible, ScreenRect(view));
    var action = view.GetNode<Button>("%ManufactureButton");
    var back = view.GetNode<Button>("%BackButton");
    Assert.True(visible.Encloses(ScreenRect(action)));
    Assert.True(visible.Encloses(ScreenRect(back)));
    var scroll = view.GetNode<ScrollContainer>("Content/Margin/Layout/Body/Details/DetailsScroll");
    Assert.True(scroll.ClipContents);
    Assert.True(view.GetNode<Label>("%CurrencyCosts").Text.Contains(ulong.MaxValue.ToString(System.Globalization.CultureInfo.CurrentCulture)));
    Assert.True(view.GetNode<Label>("%CurrencyCosts").Size.X <= scroll.Size.X);
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
  public static void EmptyCatalogExplainsNoOptionsWithoutSelectableRows()
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
  public static void SelectingRowsDisplaysRecipeDurationAndScarceStockInCatalogOrder()
  {
    var first = MakeItemData("Field scanner");
    var firstRecipe = MakeManufacturingRecipe(first, days: 1);
    first.Description = "Survey the terrain from a safe distance.";
    var second = MakeItemData("Medkit");
    var secondRecipe = MakeManufacturingRecipe(second, days: 2);
    second.Description = "Treat injured soldiers.";
    using var campaign = new GeoscapeFixture(MakeStart(
      armory: [first], manufacturingRecipes: [firstRecipe, secondRecipe]));
    campaign.State.Armory.AddStock(first, 1);
    var session = campaign.Session;
    firstRecipe.DurationDays = 3;
    var view = AddToTree(CreateEngineeringView());
    view.Present(campaign.State, session);

    var items = view.GetNode<VBoxContainer>("%ManufacturableItems");
    Assert.Equal("Field scanner", items.GetChild<Button>(0).Text);
    Assert.Equal("Medkit", items.GetChild<Button>(1).Text);
    items.GetChild<Button>(0).EmitSignal(Button.SignalName.Pressed);
    Assert.Equal("Field scanner", view.GetNode<Label>("%ItemName").Text);
    Assert.Equal(first.Description, view.GetNode<Label>("%ItemDescription").Text);
    Assert.Equal("Duration: 3d", view.GetNode<Label>("%ManufacturingDuration").Text);
    Assert.Equal("Stock: 2", view.GetNode<Label>("%Stock").Text);
    Assert.Equal("Produces 1 item.", view.GetNode<Label>("%SupplyEffect").Text);
    items.GetChild<Button>(1).EmitSignal(Button.SignalName.Pressed);
    Assert.Equal("Medkit", view.GetNode<Label>("%ItemName").Text);
    Assert.Equal(second.Description, view.GetNode<Label>("%ItemDescription").Text);
    Assert.Equal("Duration: 2d", view.GetNode<Label>("%ManufacturingDuration").Text);
    Assert.Equal("Stock: 0", view.GetNode<Label>("%Stock").Text);
  }

  [TestCase]
  public static async Task ActiveJobDisablesExistingAndNewSelectionAndIgnoresProgrammaticSignal()
  {
    await using var cleanup = new DeferredNodeCleanup();
    var first = MakeItemData("Scanner");
    var firstRecipe = MakeManufacturingRecipe(first, days: 1);
    var second = MakeItemData("Medkit");
    var secondRecipe = MakeManufacturingRecipe(second);
    using var campaign = new GeoscapeFixture(MakeStart(
      manufacturingRecipes: [firstRecipe, secondRecipe]));
    var session = campaign.Session;
    var view = AddToTree(CreateEngineeringView());
    view.Present(campaign.State, session);
    view.GetNode<VBoxContainer>("%ManufacturableItems").GetChild<Button>(0)
      .EmitSignal(Button.SignalName.Pressed);
    Assert.True(session.StartManufacturing(firstRecipe).IsRight);
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
  public static async Task ActiveRemainingTimeUsesSuppliedTickAndClampsElapsedCompletion()
  {
    await using var cleanup = new DeferredNodeCleanup();
    var item = MakeItemData("Field scanner");
    var itemRecipe = MakeManufacturingRecipe(item, days: 1);
    using var campaign = new GeoscapeFixture(MakeStart(manufacturingRecipes: [itemRecipe]));
    var session = campaign.Session;
    Assert.True(session.StartManufacturing(itemRecipe).IsRight);
    campaign.AdvanceTicks(2);
    var view = AddToTree(CreateEngineeringView());
    view.Present(campaign.State, session);
    Assert.Equal("Manufacturing: Field scanner — 23h 58m remaining", view.GetNode<Label>("%ActiveJob").Text);
    view.Present(campaign.State, session.GetManufacturingOptions(), session.ActiveManufacturing, 1443);
    Assert.Equal("Manufacturing: Field scanner — 0m remaining", view.GetNode<Label>("%ActiveJob").Text);
    Assert.Equal(2L, session.Tick);
    Assert.True(session.ActiveManufacturing.IsSome);
  }

  [TestCase]
  public static async Task PresentPreservesRecipeIdentityAcrossReorderingAndRefreshesParsedEntry()
  {
    await using var cleanup = new DeferredNodeCleanup();
    var first = MakeItemData("First");
    var firstRecipe = MakeManufacturingRecipe(first);
    var second = MakeItemData("Second");
    var secondRecipe = MakeManufacturingRecipe(second, days: 1);
    using var campaign = new GeoscapeFixture(MakeStart(
      manufacturingRecipes: [firstRecipe, secondRecipe]));
    var session = campaign.Session;
    var view = AddToTree(CreateEngineeringView());
    view.Present(campaign.State, session.GetManufacturingOptions(), session.ActiveManufacturing, session.Tick);
    view.GetNode<VBoxContainer>("%ManufacturableItems").GetChild<Button>(1)
      .EmitSignal(Button.SignalName.Pressed);
    secondRecipe.DurationDays = 2;
    using var refreshedCampaign = new GeoscapeFixture(MakeStart(
      armory: [second], manufacturingRecipes: [secondRecipe, firstRecipe]));
    refreshedCampaign.State.Armory.AddStock(second, 2);
    var refreshed = refreshedCampaign.Session;
    view.Present(refreshedCampaign.State, refreshed.GetManufacturingOptions(), refreshed.ActiveManufacturing, refreshed.Tick);

    Assert.Equal("Second", view.GetNode<Label>("%ItemName").Text);
    Assert.Equal("Duration: 2d", view.GetNode<Label>("%ManufacturingDuration").Text);
    Assert.Equal("Stock: 3", view.GetNode<Label>("%Stock").Text);
    Assert.False(view.GetNode<Button>("%ManufactureButton").Disabled);
    view.Present(refreshedCampaign.State, refreshed); // activation binding
    view.GetNode<Button>("%ManufactureButton").EmitSignal(Button.SignalName.Pressed);
    Assert.True(refreshed.ActiveManufacturing.Match(job => job.Project.Item == second, () => false));
  }

  [TestCase]
  public static async Task PresentClearsStaleSelectionWhenReplacementProducesSameItem()
  {
    await using var cleanup = new DeferredNodeCleanup();
    var item = MakeItemData("Scanner");
    var itemRecipe = MakeManufacturingRecipe(item);
    using var campaign = new GeoscapeFixture(MakeStart(manufacturingRecipes: [itemRecipe]));
    var session = campaign.Session;
    var view = AddToTree(CreateEngineeringView());
    view.Present(campaign.State, session.GetManufacturingOptions(), session.ActiveManufacturing, session.Tick);
    view.GetNode<VBoxContainer>("%ManufacturableItems").GetChild<Button>(0)
      .EmitSignal(Button.SignalName.Pressed);
    var replacementRecipe = MakeManufacturingRecipe(item);
    using var refreshedCampaign = new GeoscapeFixture(MakeStart(
      manufacturingRecipes: [replacementRecipe]));
    var refreshed = refreshedCampaign.Session;
    view.Present(refreshedCampaign.State, refreshed.GetManufacturingOptions(), refreshed.ActiveManufacturing, refreshed.Tick);

    Assert.True(view.GetNode<Button>("%ManufactureButton").Disabled);
    Assert.Equal("Select an item to manufacture.", view.GetNode<Label>("%ItemName").Text);
    Assert.Equal("", view.GetNode<Label>("%ItemDescription").Text);
    Assert.Equal("", view.GetNode<Label>("%ManufacturingDuration").Text);
    Assert.Equal("", view.GetNode<Label>("%Stock").Text);
    Assert.Equal("", view.GetNode<Label>("%SupplyEffect").Text);
    view.GetNode<Button>("%ManufactureButton").EmitSignal(Button.SignalName.Pressed); // disabled: no-op
  }

  [TestCase]
  public static async Task ActivatedUnlimitedItemDisappearsAndClearsSelectionWhileOtherItemsRemain()
  {
    await using var cleanup = new DeferredNodeCleanup();
    var supply = MakeItemData("Supply", unlimited: true);
    var supplyRecipe = MakeManufacturingRecipe(supply, days: 1);
    var scarce = MakeItemData("Medkit");
    var scarceRecipe = MakeManufacturingRecipe(scarce);
    using var campaign = new GeoscapeFixture(MakeStart(
      manufacturingRecipes: [supplyRecipe, scarceRecipe]));
    var session = campaign.Session;
    var view = AddToTree(CreateEngineeringView());
    view.Present(campaign.State, session);
    var items = view.GetNode<VBoxContainer>("%ManufacturableItems");
    Assert.Equal(2, items.GetChildCount());
    items.GetChild<Button>(0).EmitSignal(Button.SignalName.Pressed);
    Assert.Equal("Stock: Not established", view.GetNode<Label>("%Stock").Text);
    Assert.Equal("Establishes unlimited supply.", view.GetNode<Label>("%SupplyEffect").Text);
    Assert.False(view.GetNode<Button>("%ManufactureButton").Disabled);
    session.StartManufacturing(supplyRecipe).RequireRight();
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
  public static async Task RepeatedPresentReplacesRowsAndStartsOneJobPerRequest()
  {
    await using var cleanup = new DeferredNodeCleanup();
    var item = MakeItemData();
    var itemRecipe = MakeManufacturingRecipe(item);
    using var campaign = new GeoscapeFixture(MakeStart(manufacturingRecipes: [itemRecipe]));
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

  [TestCase]
  public static void FailureExplainsRejectionAndNextPresentClearsIt()
  {
    var view = AddToTree(CreateEngineeringView());
    var item = MakeItemData("Selected item");
    view.ShowFailure(item, ManufacturingStartFailure.UnknownRecipe);
    Assert.True(view.GetNode<Label>("%Status").Text.Contains(item.Name));
    Assert.True(view.GetNode<Label>("%Status").Text.Contains("UnknownRecipe"));
    using var campaign = new GeoscapeFixture();
    view.Present(campaign.State, campaign.Session);
    Assert.Equal("", view.GetNode<Label>("%Status").Text);
  }
}
