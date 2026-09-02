using Godot;
using GdUnit4;
using System;
using static GdUnit4.Assertions;

[TestSuite]
[RequireGodotRuntime]
public partial class GeoscapeViewManagerTest
{
  // A minimal IGeoscapeView root we can pack into a PackedScene for the UnitsView slot.
  private sealed partial class FakeView : Control, IGeoscapeView
  {
    private Action? _requestClose;

    public void ArmClose(Action requestClose) => _requestClose = requestClose;

    public void RequestClose() => _requestClose?.Invoke();
  }

  // An IGeoscapeViewBackdrop root whose declaration is test-controlled: SupplyBackdrop
  // toggles a non-null BackdropScene, packed lazily at first access (the manager reads it
  // during Open) so the fake needs no serialized resource graph. The proto scene's root is
  // named BackdropProto, letting assertions identify the instanced backdrop by hand.
  private sealed partial class FakeBackdropView : Control, IGeoscapeView, IGeoscapeViewBackdrop
  {
    [Export] public bool SupplyBackdrop = true;

    private PackedScene? _backdrop;
    private Action? _requestClose;

    public PackedScene? BackdropScene => SupplyBackdrop ? _backdrop ??= PackBackdrop() : null;

    public void ArmClose(Action requestClose) => _requestClose = requestClose;

    private static PackedScene PackBackdrop()
    {
      var proto = new Node3D { Name = "BackdropProto" };
      var scene = new PackedScene();
      scene.Pack(proto);
      proto.Free();
      return scene;
    }
  }

  private static GeoscapeViewManager BuildManager(bool open)
  {
    var proto = new FakeView();
    var scene = new PackedScene();
    scene.Pack(proto);
    proto.Free();

    var manager = AutoFree(new GeoscapeViewManager { UnitsView = scene });
    ((SceneTree)Engine.GetMainLoop()).Root.AddChild(manager);

    if (open)
      manager.Open(GeoscapeView.Units);
    return manager;
  }

  private static GeoscapeViewManager BuildManager(out FakeView liveView)
  {
    var manager = BuildManager(open: true);
    liveView = OnlyChild<FakeView>(manager);
    return manager;
  }

  private static GeoscapeViewManager BuildUnitManager()
  {
    var proto = new FakeView();
    var scene = new PackedScene();
    scene.Pack(proto);
    proto.Free();

    var manager = AutoFree(new GeoscapeViewManager { UnitsView = scene, UnitView = scene });
    ((SceneTree)Engine.GetMainLoop()).Root.AddChild(manager);
    return manager;
  }

  private static GeoscapeViewManager BuildBackdropManager(bool open = true, bool supplyBackdrop = true)
  {
    var proto = new FakeBackdropView { SupplyBackdrop = supplyBackdrop };
    var scene = new PackedScene();
    scene.Pack(proto);
    proto.Free();

    var manager = AutoFree(new GeoscapeViewManager { UnitsView = scene });
    ((SceneTree)Engine.GetMainLoop()).Root.AddChild(manager);

    if (open)
      manager.Open(GeoscapeView.Units);
    return manager;
  }

  // The manager hosts permanent backdrop infrastructure below the view instance, so view
  // assertions locate views by type instead of child index.
  private static T OnlyChild<T>(Node parent) where T : Node
  {
    foreach (Node child in parent.GetChildren())
      if (child is T typed)
        return typed;
    throw new Exception($"Expected one {typeof(T).Name} child under {parent.Name}, found none.");
  }

  private static bool Hosts<T>(Node parent) where T : Node
  {
    foreach (Node child in parent.GetChildren())
      if (child is T)
        return true;
    return false;
  }

  private static SubViewportContainer? BackdropLayer(GeoscapeViewManager manager)
  {
    foreach (Node child in manager.GetChildren())
      if (child is SubViewportContainer container)
        return container;
    return null;
  }

  private static SubViewport BackdropViewport(SubViewportContainer layer)
  {
    foreach (Node child in layer.GetChildren())
      if (child is SubViewport viewport)
        return viewport;
    throw new Exception($"Backdrop layer has no SubViewport child under {layer.Name}.");
  }

  [TestCase(TestName = "Open(Unit) hosts the UnitView and reports the current view")]
  public void OpenUnitHostsView()
  {
    GeoscapeViewManager manager = BuildUnitManager();
    GeoscapeView? opened = null;
    manager.ViewOpened += (view, _) => opened = view;

    manager.Open(GeoscapeView.Unit);

    Assert.Equal(GeoscapeView.Unit, manager.Current);
    Assert.Equal(GeoscapeView.Unit, opened);
    Assert.True(manager.GetChildren().AsValueEnumerable().FirstOrDefault(c => c is Control and not GeoscapeViewManager) is not null);
  }

  [TestCase(TestName = "Open(Unit) closes an open Units view first (one main view at a time)")]
  public void OpenUnitClosesUnitsFirst()
  {
    GeoscapeViewManager manager = BuildUnitManager();
    manager.Open(GeoscapeView.Units);
    var closed = new System.Collections.Generic.List<GeoscapeView>();
    manager.ViewClosed += closed.Add;

    manager.Open(GeoscapeView.Unit);

    Assert.Equal(1, closed.Count);
    Assert.Equal(GeoscapeView.Units, closed[0]);
    Assert.Equal(GeoscapeView.Unit, manager.Current);
  }

  [TestCase(TestName = "Open instantiates the view, sets Current, raises ViewOpened, adds as child")]
  public void OpenInstantiatesView()
  {
    var manager = BuildManager(open: false);
    GeoscapeView? firedView = null;
    Control? firedInstance = null;
    manager.ViewOpened += (v, inst) => { firedView = v; firedInstance = inst; };

    manager.Open(GeoscapeView.Units);
    var liveView = OnlyChild<FakeView>(manager);

    Assert.Equal(GeoscapeView.Units, manager.Current);
    Assert.True(liveView.IsInsideTree());
    Assert.Equal(GeoscapeView.Units, firedView);
    Assert.True(ReferenceEquals(liveView, firedInstance));
  }

  [TestCase(TestName = "View invoking its armed close request returns to Map, frees the instance, raises ViewClosed")]
  public void ArmedRequestClosesView()
  {
    var manager = BuildManager(out FakeView view);
    GeoscapeView? closedWith = null;
    manager.ViewClosed += v => closedWith = v;

    view.RequestClose();

    Assert.Equal(GeoscapeView.Map, manager.Current);
    Assert.False(Hosts<FakeView>(manager));
    Assert.True(view.IsQueuedForDeletion());
    Assert.Equal(GeoscapeView.Units, closedWith);
  }

  [TestCase(TestName = "Opening the already-open view is a no-op")]
  public void ReopenIsNoOp()
  {
    var manager = BuildManager(out _);
    int opened = 0;
    manager.ViewOpened += (_, _) => opened++;

    manager.Open(GeoscapeView.Units);

    Assert.Equal(0, opened);
    Assert.True(Hosts<FakeView>(manager));
  }

  [TestCase(TestName = "Open without an assigned scene throws (caller bug)")]
  public void MissingSceneThrows()
  {
    var manager = AutoFree(new GeoscapeViewManager());
    ((SceneTree)Engine.GetMainLoop()).Root.AddChild(manager);

    Assert.Throws<System.InvalidOperationException>(() => manager.Open(GeoscapeView.Units));
  }

  [TestCase(TestName = "Closing and reopening yields a fresh instance")]
  public void ReopenAfterCloseIsFresh()
  {
    var manager = BuildManager(out FakeView first);
    manager.Close();

    manager.Open(GeoscapeView.Units);
    var second = OnlyChild<FakeView>(manager);

    Assert.True(!ReferenceEquals(first, second));
  }

  [TestCase(TestName = "Open(Map) closes the active view")]
  public void OpenMapCloses()
  {
    var manager = BuildManager(out FakeView view);

    manager.Open(GeoscapeView.Map);

    Assert.Equal(GeoscapeView.Map, manager.Current);
    Assert.False(Hosts<FakeView>(manager));
    Assert.True(view.IsQueuedForDeletion());
  }

  [TestCase(TestName = "Open with a backdrop-declaring view shows the layer with the backdrop instanced in its SubViewport")]
  public void BackdropShownOnOpen()
  {
    var manager = BuildBackdropManager();

    SubViewportContainer? layer = BackdropLayer(manager);
    Assert.True(layer is not null, "Manager should host a SubViewportContainer backdrop layer.");
    Assert.True(layer!.Visible, "Backdrop layer should be visible while a backdrop view is open.");

    SubViewport viewport = BackdropViewport(layer);
    Assert.Equal(1, viewport.GetChildCount());
    Assert.True(viewport.GetChild(0).Name == "BackdropProto");
  }

  [TestCase(TestName = "A view without IGeoscapeViewBackdrop leaves the layer hidden and empty")]
  public void NoInterfaceLeavesLayerHidden()
  {
    var manager = BuildManager(open: true);

    SubViewportContainer? layer = BackdropLayer(manager);
    Assert.True(layer is not null, "Manager should host a SubViewportContainer backdrop layer.");
    Assert.False(layer!.Visible);
    Assert.Equal(0, BackdropViewport(layer).GetChildCount());
  }

  [TestCase(TestName = "A null BackdropScene is treated as no backdrop")]
  public void NullBackdropSceneTreatedAsNone()
  {
    var manager = BuildBackdropManager(supplyBackdrop: false);
    SubViewportContainer layer = BackdropLayer(manager)!;

    Assert.False(layer.Visible);
    Assert.Equal(0, BackdropViewport(layer).GetChildCount());
  }

  [TestCase(TestName = "Close frees the backdrop instance and hides the layer")]
  public void CloseFreesBackdrop()
  {
    var manager = BuildBackdropManager();
    SubViewportContainer layer = BackdropLayer(manager)!;
    Node backdrop = BackdropViewport(layer).GetChild(0);

    manager.Close();

    Assert.False(layer.Visible);
    Assert.Equal(0, BackdropViewport(layer).GetChildCount());
    Assert.True(backdrop.IsQueuedForDeletion());
  }

  [TestCase(TestName = "Reopening after close shows a fresh backdrop, not a second one")]
  public void ReopenYieldsFreshBackdrop()
  {
    var manager = BuildBackdropManager();
    SubViewportContainer layer = BackdropLayer(manager)!;
    Node first = BackdropViewport(layer).GetChild(0);
    manager.Close();

    manager.Open(GeoscapeView.Units);
    SubViewport viewport = BackdropViewport(layer);

    Assert.True(layer.Visible);
    Assert.Equal(1, viewport.GetChildCount());
    Assert.True(!ReferenceEquals(first, viewport.GetChild(0)));
    Assert.True(first.IsQueuedForDeletion());
  }
}
