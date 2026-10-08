using System.Threading.Tasks;
using FunProject.Geoscape;
using FunProject.Engineering;
using GdUnit4;
using Godot;
using static FunProject.Tests.GeoscapeTestScenes;

[TestSuite]
[RequireGodotRuntime]
public class GeoscapeProjectsIntegrationTest
{
  [TestCase(false)]
  [TestCase(true)]
  public async Task ProjectCostsUseCapturedPricesAndLiveAffordability(bool research)
  {
    await using var cleanup = new DeferredNodeCleanup();
    var currency = MakeCurrency("Budget");
    var item = MakeItemData();
    var itemRecipe = MakeManufacturingRecipe(item, costs: new() { [currency] = 10 });
    var old = MakeResearch("Old");
    var project = MakeResearch(costs: new() { [currency] = 10 });
    using var campaign = new GeoscapeFixture(MakeStart(manufacturingRecipes: [itemRecipe], researchProjects: [old, project],
      startingCurrencies: new() { [currency] = 5 }));
    var active = campaign.Session.StartResearch(old).RequireRight();
    itemRecipe.Costs[currency] = 99;
    project.Costs[currency] = 99;
    GeoscapeView view = research ? CreateResearchView() : CreateEngineeringView();
    AddToTree(view);
    view.Present(campaign.State, campaign.Session);
    var rows = view.GetNode<VBoxContainer>(research ? "%ResearchProjects" : "%ManufacturableItems");
    OnlyChild<Button>(rows).EmitSignal(Button.SignalName.Pressed);
    string text = view.GetNode<Label>("%CurrencyCosts").Text;
    Assert.True(text.Contains("Budget"));
    Assert.True(text.Contains("10"));
    Assert.True(text.Contains("5"));
    Assert.False(text.Contains("99"));
    var start = view.GetNode<Button>(research ? "%StartResearchButton" : "%ManufactureButton");
    Assert.True(start.Disabled);
    start.EmitSignal(Button.SignalName.Pressed);
    Assert.Equal(Some(active), campaign.Session.ActiveResearch);
    Assert.True(campaign.Session.ActiveManufacturing.IsNone);

    using var free = new GeoscapeFixture(MakeStart(manufacturingRecipes: [MakeManufacturingRecipe(MakeItemData(), costs: new() { [currency] = 0 })],
      researchProjects: [MakeResearch()]));
    view.Present(free.State, free.Session);
    OnlyChild<Button>(rows).EmitSignal(Button.SignalName.Pressed);
    Assert.Equal("Cost: Free", view.GetNode<Label>("%CurrencyCosts").Text);
    Assert.False(start.Disabled);
  }

  [TestCase(false)]
  [TestCase(true)]
  public async Task StaleProjectSelectionRechecksAffordability(bool research)
  {
    await using var cleanup = new DeferredNodeCleanup();
    var currency = MakeCurrency("Budget");
    var item = MakeItemData();
    var itemRecipe = MakeManufacturingRecipe(item, costs: new() { [currency] = 10 });
    var project = MakeResearch(costs: new() { [currency] = 10 });
    using var campaign = new GeoscapeFixture(MakeStart(manufacturingRecipes: [itemRecipe], researchProjects: [project],
      startingCurrencies: new() { [currency] = 10 }));
    GeoscapeView view = research ? CreateResearchView() : CreateEngineeringView();
    AddToTree(view);
    view.Present(campaign.State, campaign.Session);
    OnlyChild<Button>(view.GetNode<VBoxContainer>(research ? "%ResearchProjects" : "%ManufacturableItems"))
      .EmitSignal(Button.SignalName.Pressed);
    var start = view.GetNode<Button>(research ? "%StartResearchButton" : "%ManufactureButton");
    Assert.False(start.Disabled);
    if (research)
      campaign.Session.StartManufacturing(itemRecipe).RequireRight();
    else
      campaign.Session.StartResearch(project).RequireRight();
    campaign.ClearEvents();
    start.EmitSignal(Button.SignalName.Pressed);
    Assert.True(start.Disabled);
    Assert.Equal(0L, campaign.State.GetCurrencyBalance(currency));
    Assert.True(view.GetNode<Label>("%CurrencyCosts").Text.Contains("available 0"));
    string status = view.GetNode<Label>("%Status").Text;
    Assert.True(status.Contains("InsufficientCurrencies"));
    Assert.True(status.Contains(research ? project.Name : item.Name));
    Assert.True(research ? campaign.Session.ActiveResearch.IsNone : campaign.Session.ActiveManufacturing.IsNone);
    Assert.True(research ? campaign.Session.ActiveManufacturing.IsSome : campaign.Session.ActiveResearch.IsSome);
    Assert.Equal(0, campaign.Events.Count);
  }

  private static void PressEngineeringButton(GeoscapeScene scene)
    => scene.GetNode<GeoscapeHud>("%GeoscapeHud").GetNode<Button>("%EngineeringButton")
      .EmitSignal(Button.SignalName.Pressed);

  private static void PressResearchButton(GeoscapeScene scene)
    => scene.GetNode<GeoscapeHud>("%GeoscapeHud").GetNode<Button>("%ResearchButton")
      .EmitSignal(Button.SignalName.Pressed);

  // Synchronous tests: automatic frames cannot race manual clock steps. Completion paths
  // use visible actions; rejection paths supply snapshots through the public view contract.
  // The authored scenes supply bindings and layout; manual clock assertions run
  // synchronously, and async tests await layout/deletion boundaries.
  // A hosted view must stay in viewport pixels when the map camera moves or zooms.
  [TestCase(false)]
  [TestCase(true)]
  public async Task ProjectControlsStayInViewportWhenCameraMovesAndViewportResizes(bool research)
  {
    var scene = CreateGeoscapeScene(MakeStart());
    var viewport = CreateUiViewport(scene, new Vector2I(1600, 900));
    var manager = scene.GetNode<GeoscapeViewManager>("%ViewManager");
    if (research)
      PressResearchButton(scene);
    else
      PressEngineeringButton(scene);
    var view = manager.Current;
    await WaitForLayout(scene);
    Assert.Equal(new Rect2(0, 0, 1600, 900), ScreenRect(view));

    var camera = MapCamera(scene);
    camera.Position = new Vector2(1234, 567);
    camera.Zoom = new Vector2(2, 2);
    viewport.Size = new Vector2I(800, 600);
    camera.ForceUpdateScroll();
    await WaitForLayout(scene);

    var visible = new Rect2(0, 0, 800, 600);
    Assert.Equal(visible, ScreenRect(manager));
    Assert.Equal(visible, ScreenRect(view));
    Assert.Equal(visible, ScreenRect(MapViewport(scene))); // the map viewport tracks too
    Assert.True(visible.Encloses(ScreenRect(view.GetNode<Button>("%BackButton"))));
    view.GetNode<Button>("%BackButton").EmitSignal(Button.SignalName.Pressed);
    Assert.True(view.GetParent() is null);
    await WaitForLayout(scene);
    Assert.False(GodotObject.IsInstanceValid(view));
    Assert.True(ReferenceEquals(manager.RootView, manager.Current));
    Assert.True(scene.GetNode<GeoscapeMapControl>("%Map").IsVisibleInTree());
    var hud = scene.GetNode<GeoscapeHud>("%GeoscapeHud");
    Assert.True(hud.IsVisibleInTree());
    Assert.True(visible.Encloses(ScreenRect(hud.GetNode<Button>("%ResearchButton"))));
    Assert.True(visible.Encloses(ScreenRect(hud.GetNode<Label>("%EngineeringProgress"))));
  }

  [TestCase]
  public async Task ReopeningResearchShowsProgressAndEngineeringUnlock()
  {
    await using var cleanup = new DeferredNodeCleanup();
    var scene = AddToTree(CreateDebugGeoscapeScene());
    var manager = scene.GetNode<GeoscapeViewManager>("%ViewManager");
    PressResearchButton(scene);
    var research = (ResearchView)manager.Current;
    OnlyChild<Button>(research.GetNode<VBoxContainer>("%ResearchProjects"))
      .EmitSignal(Button.SignalName.Pressed);
    research.GetNode<Button>("%StartResearchButton").EmitSignal(Button.SignalName.Pressed);
    research.GetNode<Button>("%BackButton").EmitSignal(Button.SignalName.Pressed);
    SpeedButton(scene).EmitSignal(Button.SignalName.Pressed);
    for (int i = 0; i < 1439; i++)
      scene._PhysicsProcess(0.02);

    PressResearchButton(scene);
    var reopened = (ResearchView)manager.Current;
    Assert.Equal("Research: Prototype Kit Research - 1m remaining", reopened.GetNode<Label>("%ActiveJob").Text);
    reopened.GetNode<Button>("%BackButton").EmitSignal(Button.SignalName.Pressed);
    scene._PhysicsProcess(0.02);
    PressEngineeringButton(scene);
    var engineering = (EngineeringView)manager.Current;
    Assert.True(engineering.GetNode<VBoxContainer>("%ManufacturableItems").GetChildren()
      .AsValueEnumerable().OfType<Button>().Any(button => button.Text == "Prototype Kit"));
  }

  [TestCase]
  public async Task BrowsingFreezesManufacturingAndReturningToMapRefreshesCompletion()
  {
    await using var cleanup = new DeferredNodeCleanup();
    var scarce = MakeItemData("Scanner");
    var scarceRecipe = MakeManufacturingRecipe(scarce, days: 1);
    var unlimited = MakeItemData("Field kit", unlimited: true);
    var unlimitedRecipe = MakeManufacturingRecipe(unlimited, days: 2);
    var scene = AddToTree(CreateGeoscapeScene(MakeStart(
      manufacturingRecipes: [scarceRecipe, unlimitedRecipe])));
    var manager = scene.GetNode<GeoscapeViewManager>("%ViewManager");
    var hud = scene.GetNode<GeoscapeHud>("%GeoscapeHud");
    PressEngineeringButton(scene);
    var engineering = (EngineeringView)manager.Current;
    engineering.GetNode<VBoxContainer>("%ManufacturableItems").GetChild<Button>(0)
      .EmitSignal(Button.SignalName.Pressed);
    engineering.GetNode<Button>("%ManufactureButton").EmitSignal(Button.SignalName.Pressed);
    Assert.False(hud.IsVisibleInTree());
    hud.GetNode<Button>("%SpeedButton").EmitSignal(Button.SignalName.Pressed);
    string clock = hud.GetNode<Label>("%ClockLabel").Text;
    scene._PhysicsProcess(1.0); // browsing freezes the clock: 50 ticks at 5x would pass
    Assert.Equal(clock, hud.GetNode<Label>("%ClockLabel").Text);
    Assert.False(hud.HasNode("%EngineeringNotice"));

    engineering.GetNode<Button>("%BackButton").EmitSignal(Button.SignalName.Pressed);
    Assert.True(hud.IsVisibleInTree());
    Assert.Equal("Scanner", hud.GetNode<Label>("%EngineeringProgress").Text);
    Assert.Equal("1d remaining", hud.GetNode<Label>("%EngineeringRemaining").Text);

    // Complete on the final tick so a later TimeAdvanced cannot hide a missing refresh.
    for (int i = 0; i < 1439; i++)
      scene._PhysicsProcess(0.02);
    Assert.Equal("Scanner", hud.GetNode<Label>("%EngineeringProgress").Text);
    Assert.Equal("1d remaining", hud.GetNode<Label>("%EngineeringRemaining").Text);
    Assert.False(hud.HasNode("%EngineeringNotice"));
    scene._PhysicsProcess(0.02); // manufacturing completes at tick 1440
    Assert.Equal("No active manufacturing.", hud.GetNode<Label>("%EngineeringProgress").Text);
    Assert.False(hud.GetNode<Label>("%EngineeringRemaining").Visible);

    PressEngineeringButton(scene);
    var reopened = (EngineeringView)manager.Current;
    Assert.Equal(2, reopened.GetNode<VBoxContainer>("%ManufacturableItems").GetChildCount());
    reopened.GetNode<VBoxContainer>("%ManufacturableItems").GetChild<Button>(0)
      .EmitSignal(Button.SignalName.Pressed);
    Assert.Equal("Stock: 1", reopened.GetNode<Label>("%Stock").Text); // scarce production landed
    reopened.GetNode<VBoxContainer>("%ManufacturableItems").GetChild<Button>(1)
      .EmitSignal(Button.SignalName.Pressed);
    reopened.GetNode<Button>("%ManufactureButton").EmitSignal(Button.SignalName.Pressed);
    reopened.GetNode<Button>("%BackButton").EmitSignal(Button.SignalName.Pressed);
    Assert.Equal("Field kit", hud.GetNode<Label>("%EngineeringProgress").Text);
    Assert.Equal("2d remaining", hud.GetNode<Label>("%EngineeringRemaining").Text);
    for (int i = 0; i < 2880; i++)
      scene._PhysicsProcess(0.02); // manufacturing completes at tick 4320

    Assert.Equal("No active manufacturing.", hud.GetNode<Label>("%EngineeringProgress").Text);
    Assert.False(hud.GetNode<Label>("%EngineeringRemaining").Visible);
    PressEngineeringButton(scene);
    var supplied = (EngineeringView)manager.Current;
    Assert.Equal("Scanner", OnlyChild<Button>(supplied.GetNode<VBoxContainer>("%ManufacturableItems")).Text);
    supplied.GetNode<Button>("%BackButton").EmitSignal(Button.SignalName.Pressed);
    // Clock movement after completion also proves the session retained the selected speed.
    clock = hud.GetNode<Label>("%ClockLabel").Text;
    scene._PhysicsProcess(0.02);
    Assert.False(clock == hud.GetNode<Label>("%ClockLabel").Text);
    Assert.Equal("5x", hud.GetNode<Button>("%SpeedButton").Text);
    Assert.False(hud.GetNode<Button>("%PauseButton").ButtonPressed);
  }

  [TestCase]
  public async Task ClockStaysFrozenThroughNestedPushesAndResumesOnlyAtTheRoot()
  {
    await using var cleanup = new DeferredNodeCleanup();
    var scene = AddToTree(CreateGeoscapeScene(MakeStart(roster: [MakeEntry()])));
    var manager = scene.GetNode<GeoscapeViewManager>("%ViewManager");
    var hud = scene.GetNode<GeoscapeHud>("%GeoscapeHud");
    hud.GetNode<Button>("%SpeedButton").EmitSignal(Button.SignalName.Pressed); // 5x
    string clock = hud.GetNode<Label>("%ClockLabel").Text;

    hud.GetNode<Button>("%UnitsButton").EmitSignal(Button.SignalName.Pressed);
    var roster = (UnitRoster)manager.Current;
    scene._PhysicsProcess(1.0);
    Assert.Equal(clock, hud.GetNode<Label>("%ClockLabel").Text); // frozen while browsing

    roster.GetNode<VBoxContainer>("%UnitLabels").GetChild<UnitLabel>(0).Press();
    var unit = (UnitView)manager.Current;
    scene._PhysicsProcess(1.0);
    Assert.Equal(clock, hud.GetNode<Label>("%ClockLabel").Text); // frozen deeper

    unit.GetNode<Button>("%PathsButton").EmitSignal(Button.SignalName.Pressed);
    scene._PhysicsProcess(1.0);
    Assert.Equal(clock, hud.GetNode<Label>("%ClockLabel").Text); // frozen at three levels

    manager.Current.RequestBack(); // paths -> unit: still not the root
    scene._PhysicsProcess(1.0);
    Assert.Equal(clock, hud.GetNode<Label>("%ClockLabel").Text);
    unit.GetNode<Button>("%BackButton").EmitSignal(Button.SignalName.Pressed); // unit -> roster
    scene._PhysicsProcess(1.0);
    Assert.Equal(clock, hud.GetNode<Label>("%ClockLabel").Text);

    roster.GetNode<Button>("%BackButton").EmitSignal(Button.SignalName.Pressed); // roster -> root
    scene._PhysicsProcess(0.02); // one tick at the retained 5x speed
    Assert.False(clock == hud.GetNode<Label>("%ClockLabel").Text);
    Assert.Equal("5x", hud.GetNode<Button>("%SpeedButton").Text);
  }

  [TestCase]
  public async Task ReopeningEngineeringStartsOneJobPerRequestAndClearsCompletedSummary()
  {
    await using var cleanup = new DeferredNodeCleanup();
    var item = MakeItemData("Field kit");
    var itemRecipe = MakeManufacturingRecipe(item, days: 1);
    var scene = AddToTree(CreateGeoscapeScene(MakeStart(manufacturingRecipes: [itemRecipe])));
    var manager = scene.GetNode<GeoscapeViewManager>("%ViewManager");
    var hud = scene.GetNode<GeoscapeHud>("%GeoscapeHud");
    PressEngineeringButton(scene);
    var first = (EngineeringView)manager.Current;
    Assert.Equal(1, first.GetNode<VBoxContainer>("%ManufacturableItems").GetChildCount());
    first.GetNode<VBoxContainer>("%ManufacturableItems").GetChild<Button>(0).EmitSignal(Button.SignalName.Pressed);
    first.GetNode<Button>("%ManufactureButton").EmitSignal(Button.SignalName.Pressed);
    Assert.Equal("", first.GetNode<Label>("%Status").Text);
    first.GetNode<Button>("%BackButton").EmitSignal(Button.SignalName.Pressed);
    hud.GetNode<Button>("%SpeedButton").EmitSignal(Button.SignalName.Pressed);
    for (int i = 0; i < 1440; i++) // one day at 5x: exactly one tick per call
      scene._PhysicsProcess(0.02);
    Assert.Equal("No active manufacturing.", hud.GetNode<Label>("%EngineeringProgress").Text);
    Assert.False(hud.GetNode<Label>("%EngineeringRemaining").Visible);

    PressEngineeringButton(scene);
    var second = (EngineeringView)manager.Current;
    second.GetNode<VBoxContainer>("%ManufacturableItems").GetChild<Button>(0).EmitSignal(Button.SignalName.Pressed);
    Assert.Equal("Stock: 1", second.GetNode<Label>("%Stock").Text);
    second.GetNode<Button>("%ManufactureButton").EmitSignal(Button.SignalName.Pressed);
    Assert.Equal("", second.GetNode<Label>("%Status").Text); // duplicate handlers would show Busy
    Assert.True(second.GetNode<Button>("%ManufactureButton").Disabled);
    Assert.Equal("Field kit", hud.GetNode<Label>("%EngineeringProgress").Text);
    Assert.Equal("1d remaining", hud.GetNode<Label>("%EngineeringRemaining").Text);
    second.GetNode<Button>("%BackButton").EmitSignal(Button.SignalName.Pressed);
    for (int i = 0; i < 1440; i++)
      scene._PhysicsProcess(0.02);
    PressEngineeringButton(scene);
    var third = (EngineeringView)manager.Current;
    third.GetNode<VBoxContainer>("%ManufacturableItems").GetChild<Button>(0).EmitSignal(Button.SignalName.Pressed);
    Assert.Equal("Stock: 2", third.GetNode<Label>("%Stock").Text);
  }

  [TestCase]
  public async Task RejectedManufacturingRequestRestoresCampaignSnapshotThenShowsFailure()
  {
    await using var cleanup = new DeferredNodeCleanup();
    var scene = AddToTree(CreateGeoscapeScene(MakeStart()));
    // _Ready initializes the idle project status before any view action or foreign snapshot.
    var hud = scene.GetNode<GeoscapeHud>("%GeoscapeHud");
    Assert.Equal("No active manufacturing.", hud.GetNode<Label>("%EngineeringProgress").Text);
    Assert.False(hud.HasNode("%EngineeringNotice"));

    var manager = scene.GetNode<GeoscapeViewManager>("%ViewManager");
    PressEngineeringButton(scene);
    var view = (EngineeringView)manager.Current;
    var item = MakeItemData();
    var itemRecipe = MakeManufacturingRecipe(item);
    using var foreignCampaign = new GeoscapeFixture(MakeStart(manufacturingRecipes: [itemRecipe]));
    var foreign = foreignCampaign.Session;
    view.Present(foreign.GetManufacturingOptions(), None, 0);
    view.GetNode<VBoxContainer>("%ManufacturableItems").GetChild<Button>(0).EmitSignal(Button.SignalName.Pressed);
    view.GetNode<Button>("%ManufactureButton").EmitSignal(Button.SignalName.Pressed);

    Assert.Equal("No items available to manufacture.",
      OnlyChild<Label>(view.GetNode<VBoxContainer>("%ManufacturableItems")).Text);
    Assert.Equal("No active manufacturing.", view.GetNode<Label>("%ActiveJob").Text);
    Assert.True(view.GetNode<Label>("%Status").Text.Contains("UnknownRecipe"));
    Assert.True(view.GetNode<Label>("%Status").Text.Contains(item.Name));
  }
}
