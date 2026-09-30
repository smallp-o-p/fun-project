using System.Threading.Tasks;
using GdUnit4;
using Godot;
using static FunProject.Tests.GeoscapeTestScenes;

[TestSuite]
[RequireGodotRuntime]
public class GeoscapeProjectsIntegrationTest
{
  private static void PressEngineeringButton(GeoscapeScene scene)
    => scene.GetNode<GeoscapeHud>("%GeoscapeHud").GetNode<Button>("%EngineeringButton")
      .EmitSignal(Button.SignalName.Pressed);

  // Synchronous tests: automatic frames cannot race manual clock steps. Completion paths
  // use visible actions; rejection paths supply snapshots through the public view contract.
  // The authored scenes supply bindings and layout; manual clock assertions run
  // synchronously, and async tests await layout/deletion boundaries.
  // A hosted view must stay in viewport pixels when the map camera moves or zooms.
  [TestCase]
  public async Task ProjectControlsStayInViewportWhenCameraMovesAndViewportResizes()
  {
    var scene = CreateGeoscapeScene(MakeStart());
    var viewport = CreateUiViewport(scene, new Vector2I(1600, 900));
    var manager = scene.GetNode<GeoscapeViewManager>("%ViewManager");
    PressEngineeringButton(scene);
    var view = (EngineeringView)manager.Current;
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
    Assert.True(scene.GetNode<GeoscapeHud>("%GeoscapeHud").IsVisibleInTree());
  }

  [TestCase]
  public async Task BrowsingFreezesManufacturingAndReturningToMapRefreshesCompletion()
  {
    await using var cleanup = new DeferredNodeCleanup();
    var scarce = MakeItemData("Scanner", manufacturingDays: 1);
    var unlimited = MakeItemData("Field kit", unlimited: true, manufacturingDays: 2);
    var scene = AddToTree(CreateGeoscapeScene(MakeStart(
      manufacturableItems: [scarce, unlimited])));
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
    Assert.Equal("", hud.GetNode<Label>("%EngineeringNotice").Text);

    engineering.GetNode<Button>("%BackButton").EmitSignal(Button.SignalName.Pressed);
    Assert.True(hud.IsVisibleInTree());
    Assert.True(hud.GetNode<Label>("%EngineeringProgress").Text.Contains("Scanner — 1d 0h 0m"));

    // Complete on the final tick so a later TimeAdvanced cannot hide a missing refresh.
    for (int i = 0; i < 1439; i++)
      scene._PhysicsProcess(0.02);
    Assert.True(hud.GetNode<Label>("%EngineeringProgress").Text.Contains("Scanner — 1m"));
    Assert.Equal("", hud.GetNode<Label>("%EngineeringNotice").Text);
    scene._PhysicsProcess(0.02); // manufacturing completes at tick 1440
    Assert.Equal("No active manufacturing.", hud.GetNode<Label>("%EngineeringProgress").Text);
    Assert.True(hud.GetNode<Label>("%EngineeringNotice").Text.Contains("Scanner"));

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
    Assert.True(hud.GetNode<Label>("%EngineeringProgress").Text.Contains("Field kit — 2d 0h 0m"));
    for (int i = 0; i < 2880; i++)
      scene._PhysicsProcess(0.02); // manufacturing completes at tick 4320

    Assert.Equal("No active manufacturing.", hud.GetNode<Label>("%EngineeringProgress").Text);
    Assert.True(hud.GetNode<Label>("%EngineeringNotice").Text.Contains("Field kit"));
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
  public async Task ReopeningEngineeringStartsOneJobPerRequestAndPreservesItsPreviousNotice()
  {
    await using var cleanup = new DeferredNodeCleanup();
    var item = MakeItemData("Field kit", manufacturingDays: 1);
    var scene = AddToTree(CreateGeoscapeScene(MakeStart(manufacturableItems: [item])));
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
    string notice = hud.GetNode<Label>("%EngineeringNotice").Text;
    Assert.True(notice.Contains("Field kit"));

    PressEngineeringButton(scene);
    var second = (EngineeringView)manager.Current;
    second.GetNode<VBoxContainer>("%ManufacturableItems").GetChild<Button>(0).EmitSignal(Button.SignalName.Pressed);
    Assert.Equal("Stock: 1", second.GetNode<Label>("%Stock").Text);
    second.GetNode<Button>("%ManufactureButton").EmitSignal(Button.SignalName.Pressed);
    Assert.Equal("", second.GetNode<Label>("%Status").Text); // duplicate handlers would show Busy
    Assert.True(second.GetNode<Button>("%ManufactureButton").Disabled);
    Assert.Equal(notice, hud.GetNode<Label>("%EngineeringNotice").Text);
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
    Assert.Equal("", hud.GetNode<Label>("%EngineeringNotice").Text);

    var manager = scene.GetNode<GeoscapeViewManager>("%ViewManager");
    PressEngineeringButton(scene);
    var view = (EngineeringView)manager.Current;
    var item = MakeItemData();
    using var foreignCampaign = new GeoscapeFixture(MakeStart(manufacturableItems: [item]));
    var foreign = foreignCampaign.Session;
    view.Present(foreign.GetManufacturingOptions(), None, 0);
    view.GetNode<VBoxContainer>("%ManufacturableItems").GetChild<Button>(0).EmitSignal(Button.SignalName.Pressed);
    view.GetNode<Button>("%ManufactureButton").EmitSignal(Button.SignalName.Pressed);

    Assert.Equal("No items available to manufacture.",
      OnlyChild<Label>(view.GetNode<VBoxContainer>("%ManufacturableItems")).Text);
    Assert.Equal("No active manufacturing.", view.GetNode<Label>("%ActiveJob").Text);
    Assert.Equal("This item is not available for manufacturing.",
      view.GetNode<Label>("%Status").Text);
  }
}
