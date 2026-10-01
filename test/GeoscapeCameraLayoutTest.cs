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
  public async Task InitialFrameFitsTheWholeMapAboveTheBottomHud(int width, int height)
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

  private static void AssertMapFitsOperationalArea(GeoscapeScene scene)
  {
    var container = MapViewport(scene);
    var bottomHud = scene.GetNode<GeoscapeHud>("%GeoscapeHud").GetNode<Control>("BottomBar");
    Vector2 hudTop = container.GetGlobalTransformWithCanvas().AffineInverse()
      * bottomHud.GetGlobalTransformWithCanvas().Origin;
    var available = new Rect2(Vector2.Zero, new Vector2(container.Size.X, hudTop.Y));
    Rect2 renderedMap = MapCamera(scene).GetViewport().CanvasTransform
      * new Rect2(Vector2.Zero, (Vector2)scene.Start!.Map.Size);

    Assert.True(available.Grow(0.1f).Encloses(renderedMap),
      $"Map {renderedMap} must fit above bottom HUD in {available}.");
    Assert.True(renderedMap.GetCenter().DistanceTo(available.GetCenter()) < 0.1f,
      "The map must be centered in the unobscured operational area.");
    Assert.True(Mathf.IsEqualApprox(renderedMap.Size.X, available.Size.X)
      || Mathf.IsEqualApprox(renderedMap.Size.Y, available.Size.Y),
      "Use the largest uniform zoom that shows the whole map.");
  }
}
