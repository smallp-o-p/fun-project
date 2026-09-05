using System.Threading.Tasks;
using GdUnit4;
using Godot;
using static FunProject.Tests.GeoscapeUiTestFactory;

[TestSuite]
[RequireGodotRuntime]
public class GeoscapeProjectsIntegrationTest
{
  // Synchronous tests: automatic frames cannot race manual clock steps. Completion paths
  // use visible actions; rejection paths supply snapshots through the public view contract.
  // These fixtures exercise the real controllers; authored scene wiring is checked in-game.
  // A hosted view must stay in viewport pixels when the map camera moves or zooms.
  [TestCase]
  public async Task ProjectControlsStayInViewportWhenCameraMovesAndViewportResizes()
  {
    var scene = CreateGeoscapeScene(MakeStart());
    var viewport = CreateUiViewport(scene, new Vector2I(1600, 900));
    var manager = scene.GetNode<GeoscapeViewManager>("%ViewManager");
    manager.Open(GeoscapeView.Engineering);
    var view = OnlyChild<EngineeringView>(manager);
    await WaitForLayout(scene);
    Assert.Equal(new Rect2(0, 0, 1600, 900), ScreenRect(view));

    var camera = scene.GetNode<GeoscapeCameraRig>("Camera");
    camera.Position = new Vector2(1234, 567);
    camera.Zoom = new Vector2(2, 2);
    viewport.Size = new Vector2I(800, 600);
    camera.ForceUpdateScroll();
    await WaitForLayout(scene);

    var visible = new Rect2(0, 0, 800, 600);
    Assert.Equal(visible, ScreenRect(manager));
    Assert.Equal(visible, ScreenRect(view));
    Assert.True(visible.Encloses(ScreenRect(view.GetNode<Button>("%BackButton"))));
    var backdrop = manager.GetNode<SubViewportContainer>("Backdrop");
    Assert.Equal(visible, ScreenRect(backdrop));
    view.GetNode<Button>("%BackButton").EmitSignal(Button.SignalName.Pressed);
    Assert.True(view.GetParent() is null);
    await WaitForLayout(scene);
    Assert.False(GodotObject.IsInstanceValid(view));
    Assert.Equal(GeoscapeView.Map, manager.Current);
    Assert.True(scene.GetNode<GeoscapeHud>("%GeoscapeHud").Visible);
  }

  [TestCase]
  public void ReadyInitializesIdleProjectStatus()
  {
    var scene = AddToTree(CreateGeoscapeScene(MakeStart()));
    var hud = scene.GetNode<GeoscapeHud>("%GeoscapeHud");
    Assert.Equal("No active manufacturing.", hud.GetNode<Label>("%EngineeringProgress").Text);
    Assert.Equal("", hud.GetNode<Label>("%EngineeringNotice").Text);
  }

  [TestCase]
  public void BrowsingFreezesManufacturingAndReturningToMapRefreshesCompletion()
  {
    var scarce = MakeItemData("Scanner", manufacturingDays: 1);
    var unlimited = MakeItemData("Field kit", unlimited: true, manufacturingDays: 2);
    var scene = AddToTree(CreateGeoscapeScene(MakeStart(
      manufacturableItems: [scarce, unlimited])));
    var manager = scene.GetNode<GeoscapeViewManager>("%ViewManager");
    var hud = scene.GetNode<GeoscapeHud>("%GeoscapeHud");
    hud.GetNode<Button>("%EngineeringButton").EmitSignal(Button.SignalName.Pressed);
    var engineering = OnlyChild<EngineeringView>(manager);
    engineering.GetNode<VBoxContainer>("%ManufacturableItems").GetChild<Button>(0)
      .EmitSignal(Button.SignalName.Pressed);
    engineering.GetNode<Button>("%ManufactureButton").EmitSignal(Button.SignalName.Pressed);
    Assert.False(hud.Visible);
    hud.GetNode<Button>("%SpeedButton").EmitSignal(Button.SignalName.Pressed);
    string clock = hud.GetNode<Label>("%ClockLabel").Text;
    scene._PhysicsProcess(1.0); // browsing freezes the clock: 50 ticks at 5x would pass
    Assert.Equal(clock, hud.GetNode<Label>("%ClockLabel").Text);
    Assert.Equal("", hud.GetNode<Label>("%EngineeringNotice").Text);

    engineering.GetNode<Button>("%BackButton").EmitSignal(Button.SignalName.Pressed);
    Assert.True(hud.Visible);
    Assert.True(hud.GetNode<Label>("%EngineeringProgress").Text.Contains("Scanner — 1d 0h 0m"));

    // Complete on the final tick so a later TimeAdvanced cannot hide a missing refresh.
    for (int i = 0; i < 1439; i++)
      scene._PhysicsProcess(0.02);
    Assert.True(hud.GetNode<Label>("%EngineeringProgress").Text.Contains("Scanner — 1m"));
    Assert.Equal("", hud.GetNode<Label>("%EngineeringNotice").Text);
    scene._PhysicsProcess(0.02); // manufacturing completes at tick 1440
    Assert.Equal("No active manufacturing.", hud.GetNode<Label>("%EngineeringProgress").Text);
    Assert.True(hud.GetNode<Label>("%EngineeringNotice").Text.Contains("Scanner"));

    manager.Open(GeoscapeView.Engineering);
    var reopened = OnlyChild<EngineeringView>(manager);
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
    manager.Open(GeoscapeView.Engineering);
    var supplied = OnlyChild<EngineeringView>(manager);
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
  public void ReopeningEngineeringStartsOneJobPerRequestAndPreservesItsPreviousNotice()
  {
    var item = MakeItemData("Field kit", manufacturingDays: 1);
    var scene = AddToTree(CreateGeoscapeScene(MakeStart(manufacturableItems: [item])));
    var manager = scene.GetNode<GeoscapeViewManager>("%ViewManager");
    var hud = scene.GetNode<GeoscapeHud>("%GeoscapeHud");
    manager.Open(GeoscapeView.Engineering);
    var first = OnlyChild<EngineeringView>(manager);
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

    manager.Open(GeoscapeView.Engineering);
    var second = OnlyChild<EngineeringView>(manager);
    second.GetNode<VBoxContainer>("%ManufacturableItems").GetChild<Button>(0).EmitSignal(Button.SignalName.Pressed);
    Assert.Equal("Stock: 1", second.GetNode<Label>("%Stock").Text);
    second.GetNode<Button>("%ManufactureButton").EmitSignal(Button.SignalName.Pressed);
    Assert.Equal("", second.GetNode<Label>("%Status").Text); // duplicate handlers would show Busy
    Assert.True(second.GetNode<Button>("%ManufactureButton").Disabled);
    Assert.Equal(notice, hud.GetNode<Label>("%EngineeringNotice").Text);
    second.GetNode<Button>("%BackButton").EmitSignal(Button.SignalName.Pressed);
    for (int i = 0; i < 1440; i++)
      scene._PhysicsProcess(0.02);
    manager.Open(GeoscapeView.Engineering);
    var third = OnlyChild<EngineeringView>(manager);
    third.GetNode<VBoxContainer>("%ManufacturableItems").GetChild<Button>(0).EmitSignal(Button.SignalName.Pressed);
    Assert.Equal("Stock: 2", third.GetNode<Label>("%Stock").Text);
  }

  [TestCase]
  public void RejectedManufacturingRequestRestoresCampaignSnapshotThenShowsFailure()
  {
    var scene = AddToTree(CreateGeoscapeScene(MakeStart()));
    var manager = scene.GetNode<GeoscapeViewManager>("%ViewManager");
    manager.Open(GeoscapeView.Engineering);
    var view = OnlyChild<EngineeringView>(manager);
    var item = MakeItemData();
    using var foreignCampaign = new GeoscapeFixture(MakeStart(manufacturableItems: [item]));
    var foreign = foreignCampaign.Session;
    view.Present(foreign.GetManufacturingOptions(), None, 0);
    view.GetNode<VBoxContainer>("%ManufacturableItems").GetChild<Button>(0).EmitSignal(Button.SignalName.Pressed);
    view.GetNode<Button>("%ManufactureButton").EmitSignal(Button.SignalName.Pressed);

    Assert.Equal("No items available to manufacture.",
      OnlyChild<Label>(view.GetNode<VBoxContainer>("%ManufacturableItems")).Text);
    Assert.Equal("No active manufacturing.", view.GetNode<Label>("%ActiveJob").Text);
    Assert.True(view.GetNode<Label>("%Status").Text.Contains("not part of this campaign"));
  }
}
