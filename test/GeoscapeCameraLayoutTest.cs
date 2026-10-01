using System.Threading.Tasks;
using Godot;
using GdUnit4;
using static FunProject.Tests.GeoscapeTestScenes;

namespace FunProject.Tests;

[TestSuite]
[RequireGodotRuntime]
public class GeoscapeCameraLayoutTest
{
  [TestCase(800, 600)]
  [TestCase(1280, 720)]
  [TestCase(1920, 1080)]
  public async Task InitialFrameFitsTheWholeMapBetweenHudRows(int width, int height)
  {
    var scene = CreateGeoscapeScene(TestData.MakeStart());
    var viewport = CreateUiViewport(scene, new Vector2I(width, height));
    await WaitForLayout(viewport);

    AssertMapFitsOperationalArea(scene);
    Assert.Equal(new Vector2(width, height), MapCamera(scene).GetViewport().GetVisibleRect().Size);
  }

  [TestCase]
  public async Task LiveResizeRefitsTheSameMapInBothDirections()
  {
    var scene = CreateGeoscapeScene(TestData.MakeStart());
    var viewport = CreateUiViewport(scene, new Vector2I(1920, 1080));
    await WaitForLayout(viewport);
    var camera = MapCamera(scene);

    foreach (var size in new[] { new Vector2I(800, 600), new Vector2I(1280, 720), new Vector2I(1920, 1080) })
    {
      viewport.Size = size;
      await WaitForLayout(viewport);
      Assert.True(ReferenceEquals(camera, MapCamera(scene)));
      AssertMapFitsOperationalArea(scene);
    }
  }

  [TestCase]
  public async Task HudGeometryChangeRefitsWithoutAViewportResize()
  {
    var scene = CreateGeoscapeScene(TestData.MakeStart());
    var viewport = CreateUiViewport(scene, new Vector2I(1280, 720));
    await WaitForLayout(viewport);
    var bottomHud = scene.GetNode<GeoscapeHud>("%GeoscapeHud").GetNode<Control>("BottomBar");

    bottomHud.OffsetTop -= 80;
    await WaitForLayout(viewport);

    AssertMapFitsOperationalArea(scene);
  }

  [TestCase]
  public async Task ResizeWhileCoveredRefitsBeforeReturningToTheMap()
  {
    await using var cleanup = new DeferredNodeCleanup();
    var scene = CreateGeoscapeScene(TestData.MakeStart());
    var viewport = CreateUiViewport(scene, new Vector2I(1920, 1080));
    var manager = scene.GetNode<GeoscapeViewManager>("%ViewManager");
    await WaitForLayout(viewport);
    manager.Push(CreateBaseView());

    viewport.Size = new Vector2I(800, 600);
    await WaitForLayout(viewport);
    manager.Pop();
    await WaitForLayout(viewport);

    AssertMapFitsOperationalArea(scene);
  }

  [TestCase]
  public async Task ManualCameraPositionAndZoomSurviveFramesAndLiveResize()
  {
    var scene = CreateGeoscapeScene(TestData.MakeStart());
    var viewport = CreateUiViewport(scene, new Vector2I(1920, 1080));
    await WaitForLayout(viewport);
    var camera = MapCamera(scene);
    var position = new Vector2(1234, 567);
    var zoom = new Vector2(2, 2);
    camera.Position = position;
    camera.Zoom = zoom;
    await WaitForLayout(viewport);
    Assert.Equal(position, camera.Position);
    Assert.Equal(zoom, camera.Zoom);

    viewport.Size = new Vector2I(800, 600);
    await WaitForLayout(viewport);

    Assert.Equal(position, camera.Position);
    Assert.Equal(zoom, camera.Zoom);
  }

  [TestCase(8)]
  [TestCase(1480)]
  public async Task UpperCornerMapEventRemainsClickableBelowTimeControls(int x)
  {
    await using var cleanup = new DeferredNodeCleanup();
    var scene = CreateGeoscapeScene(TestData.MakeStart(
      regions: [TestData.MakeRegion("Corner")],
      timeline: [TestData.MakeScheduled(1, TestData.MakeEvent("Corner event", targetRegionName: "Corner"))]));
    var region = new RegionButton { Name = "Corner" };
    region.AddChild(new Polygon2D
    {
      Name = "Fill",
      Polygon = [new(x, 8), new(x + 40, 8), new(x + 40, 48), new(x, 48)],
    });
    scene.GetNode<GeoscapeMapControl>("%Map").AddChild(region);
    var viewport = CreateUiViewport(scene, new Vector2I(800, 600));
    SpeedButton(scene).EmitSignal(BaseButton.SignalName.Pressed);
    scene._PhysicsProcess(0.02);
    await WaitForLayout(viewport);

    var marker = MapEventMarkers(scene)[0];
    Vector2 center = MapViewport(scene).GetGlobalTransformWithCanvas()
      * ScreenRect(marker).GetCenter();
    viewport.NotifyMouseEntered();
    viewport.PushInput(new InputEventMouseMotion { Position = center, GlobalPosition = center }, true);
    foreach (bool pressed in new[] { true, false })
      viewport.PushInput(new InputEventMouseButton
      {
        ButtonIndex = MouseButton.Left,
        Position = center,
        GlobalPosition = center,
        Pressed = pressed,
      }, true);

    var current = scene.GetNode<GeoscapeViewManager>("%ViewManager").Current;
    Assert.True(current is GeoscapeEventResolution,
      "The time controls must not intercept clicks on upper-corner map events.");
    Assert.Equal("Corner event", current.GetNode<Label>("%Title").Text);
  }

  private static void AssertMapFitsOperationalArea(GeoscapeScene scene)
  {
    var container = MapViewport(scene);
    var bottomHud = scene.GetNode<GeoscapeHud>("%GeoscapeHud").GetNode<Control>("BottomBar");
    Vector2 hudTop = container.GetGlobalTransformWithCanvas().AffineInverse()
      * bottomHud.GetGlobalTransformWithCanvas().Origin;
    var timePanel = scene.GetNode<GeoscapeHud>("%GeoscapeHud").GetNode<Control>("TimePanel");
    Vector2 timeBottom = container.GetGlobalTransformWithCanvas().AffineInverse()
      * (timePanel.GetGlobalTransformWithCanvas() * new Vector2(0, timePanel.Size.Y));
    var available = new Rect2(new Vector2(0, timeBottom.Y),
      new Vector2(container.Size.X, hudTop.Y - timeBottom.Y));
    Rect2 renderedMap = MapCamera(scene).GetViewport().CanvasTransform
      * new Rect2(Vector2.Zero, (Vector2)scene.Start!.Map.Size);

    Assert.True(available.Grow(0.1f).Encloses(renderedMap),
      $"Map {renderedMap} must fit between the time controls and bottom HUD in {available}.");
    Assert.True(renderedMap.GetCenter().DistanceTo(available.GetCenter()) < 0.1f,
      "The map must be centered in the unobscured operational area.");
    Assert.True(Mathf.IsEqualApprox(renderedMap.Size.X, available.Size.X)
      || Mathf.IsEqualApprox(renderedMap.Size.Y, available.Size.Y),
      "Use the largest uniform zoom that shows the whole map.");
  }
}
