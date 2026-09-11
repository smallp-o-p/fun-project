#nullable disable warnings
using System;
using System.Threading.Tasks;
using CampaignGameState = global::FunProject.GameState.GameState;
using FunProject.Strategic;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;
using static FunProject.Tests.GeoscapeTestScenes;

[TestSuite]
[RequireGodotRuntime]
public partial class GeoscapeViewManagerTest
{
  // Minimal stack member with retained local state and a Present counter.
  private sealed partial class FakeView : GeoscapeView
  {
    public string Marker = "";
    public int Presents;

    public override void Present(CampaignGameState state, GeoscapeSession session)
      => Presents++;
  }

  private static GeoscapeViewManager BuildManager()
  {
    var root = new FakeView { Name = "Root" };
    var manager = AutoFree(new GeoscapeViewManager { RootView = root });
    manager.AddChild(root); // the authored shape: RootView is a direct child
    ((SceneTree)Engine.GetMainLoop()).Root.AddChild(manager);
    return manager;
  }

  // A code-built manager with no AutoFree: this suite frees it via QueueFree itself.
  private static GeoscapeViewManager BuildSelfFreeingManager()
  {
    var root = new FakeView { Name = "Root" };
    var manager = new GeoscapeViewManager { RootView = root };
    manager.AddChild(root);
    ((SceneTree)Engine.GetMainLoop()).Root.AddChild(manager);
    return manager;
  }

  [TestCase]
  public async Task OpeningAViewHidesTheMapAndHud()
  {
    await using var cleanup = new DeferredNodeCleanup();
    var scene = AddToTree(CreateGeoscapeScene(MakeStart()));
    var manager = scene.GetNode<GeoscapeViewManager>("%ViewManager");
    var hud = scene.GetNode<GeoscapeHud>("%GeoscapeHud");

    hud.GetNode<Button>("%EngineeringButton").EmitSignal(Button.SignalName.Pressed);

    Assert.True(manager.Current is EngineeringView);
    Assert.False(MapViewport(scene).IsVisibleInTree()); // the covered map viewport stops rendering
    Assert.False(hud.IsVisibleInTree());

    ((EngineeringView)manager.Current).GetNode<Button>("%BackButton")
      .EmitSignal(Button.SignalName.Pressed);
    Assert.True(MapViewport(scene).IsVisibleInTree());
    Assert.True(hud.IsVisibleInTree());
    Assert.True(ReferenceEquals(manager.RootView, manager.Current));
  }

  [TestCase]
  public async Task PushShowsOnlyTheTopViewAndDisablesCoveredViews()
  {
    await using var cleanup = new DeferredNodeCleanup();
    var manager = BuildManager();
    var root = manager.RootView;
    var a = new FakeView { Name = "A" };
    var b = new FakeView { Name = "B" };

    manager.Push(a);
    manager.Push(b);

    Assert.True(ReferenceEquals(b, manager.Current));
    Assert.True(b.IsVisibleInTree());
    Assert.Equal(Node.ProcessModeEnum.Inherit, b.ProcessMode);
    Assert.False(a.IsVisibleInTree());
    Assert.Equal(Node.ProcessModeEnum.Disabled, a.ProcessMode);
    Assert.False(root.IsVisibleInTree());
    Assert.Equal(Node.ProcessModeEnum.Disabled, root.ProcessMode);

    manager.Pop();
    Assert.True(ReferenceEquals(a, manager.Current));
    Assert.True(a.IsVisibleInTree());
    Assert.Equal(Node.ProcessModeEnum.Inherit, a.ProcessMode);
    Assert.True(b.GetParent() is null && b.IsQueuedForDeletion());

    manager.Pop();
    Assert.True(ReferenceEquals(root, manager.Current));
    Assert.True(root.IsVisibleInTree());
  }

  [TestCase]
  public async Task PushPreservesAuthoredLayoutAcrossCoverAndPop()
  {
    await using var cleanup = new DeferredNodeCleanup();
    var manager = BuildManager();
    var view = new FakeView { Name = "AuthoredLayout" };
    view.AnchorLeft = 0.2f;
    view.AnchorTop = 0.3f;
    view.AnchorRight = 0.8f;
    view.AnchorBottom = 0.9f;
    view.OffsetLeft = 11;
    view.OffsetTop = 13;
    view.OffsetRight = -17;
    view.OffsetBottom = -19;

    manager.Push(view);
    manager.Push(new FakeView { Name = "Cover" });
    manager.Pop();

    Assert.Equal(0.2f, view.AnchorLeft);
    Assert.Equal(0.3f, view.AnchorTop);
    Assert.Equal(0.8f, view.AnchorRight);
    Assert.Equal(0.9f, view.AnchorBottom);
    Assert.Equal(11f, view.OffsetLeft);
    Assert.Equal(13f, view.OffsetTop);
    Assert.Equal(-17f, view.OffsetRight);
    Assert.Equal(-19f, view.OffsetBottom);
  }

  [TestCase]
  public async Task BackReturnsToTheSameCoveredInstanceWithRetainedState()
  {
    await using var cleanup = new DeferredNodeCleanup();
    var manager = BuildManager();
    var a = new FakeView { Name = "A", Marker = "kept" };
    manager.Push(a);
    manager.Push(new FakeView { Name = "B" });

    manager.Pop();

    var restored = (FakeView)manager.Current;
    Assert.True(ReferenceEquals(a, restored));
    Assert.Equal("kept", restored.Marker);
  }

  [TestCase]
  public void BackOnTheRootIsASafeNoOp()
  {
    var manager = BuildManager();
    var root = manager.RootView;
    int changed = 0;
    manager.ViewChanged += _ => changed++;

    manager.Pop();
    root.RequestBack();

    Assert.True(ReferenceEquals(root, manager.Current));
    Assert.True(root.IsVisibleInTree());
    Assert.Equal(0, changed);
  }

  [TestCase]
  public async Task AuthoredBackButtonPopsItsViewOnce()
  {
    await using var cleanup = new DeferredNodeCleanup();
    var manager = BuildManager();
    var button = new Button { Name = "BackButton" };
    var view = new GeoscapeView { Name = "View", BackButton = button };
    view.AddChild(button);
    manager.Push(view);
    int requests = 0;
    view.BackRequested += () => requests++;

    button.EmitSignal(BaseButton.SignalName.Pressed);

    Assert.Equal(1, requests);
    Assert.True(ReferenceEquals(manager.RootView, manager.Current));
  }

  [TestCase]
  public void RootWithoutBackButtonIsValid()
  {
    var root = new GeoscapeView { Name = "Root" };
    var manager = AutoFree(new GeoscapeViewManager { RootView = root });
    manager.AddChild(root);
    ((SceneTree)Engine.GetMainLoop()).Root.AddChild(manager);

    Assert.True(ReferenceEquals(root, manager.Current));
  }

  [TestCase]
  public async Task InvalidPushesLeaveTheStackUnchanged()
  {
    await using var cleanup = new DeferredNodeCleanup();
    var manager = BuildManager();
    var a = new FakeView { Name = "A" };
    manager.Push(a);
    var b = new FakeView { Name = "B" };
    manager.Push(b);

    var parented = new FakeView { Name = "Parented" };
    manager.AddChild(parented); // now owned by the manager, not a fresh view
    var queued = new FakeView { Name = "Queued" };
    queued.QueueFree();
    var freed = new FakeView { Name = "Freed" };
    freed.Free();

    Assert.Throws<InvalidOperationException>(() => manager.Push(a)); // already stacked (covered)
    Assert.Throws<InvalidOperationException>(() => manager.Push(b)); // already stacked (current)
    Assert.Throws<InvalidOperationException>(() => manager.Push(manager.RootView)); // root instance
    Assert.Throws<InvalidOperationException>(() => manager.Push(parented));
    Assert.Throws<InvalidOperationException>(() => manager.Push(queued));
    Assert.Throws<InvalidOperationException>(() => manager.Push(freed));
    Assert.Throws<ArgumentNullException>(() => manager.Push(null!));

    Assert.True(ReferenceEquals(b, manager.Current));
    Assert.True(b.IsVisibleInTree());
    manager.Pop();
    Assert.True(ReferenceEquals(a, manager.Current));
    manager.Pop();
    Assert.True(ReferenceEquals(manager.RootView, manager.Current));
  }

  [TestCase]
  public void MissingOrDetachedRootViewIsAnAuthoringError()
  {
    var bare = AutoFree(new GeoscapeViewManager());
    Assert.Throws<InvalidOperationException>(() => bare._Ready());

    var root = AutoFree(new FakeView { Name = "Root" });
    var detachedRoot = AutoFree(new GeoscapeViewManager { RootView = root });
    Assert.Throws<InvalidOperationException>(() => detachedRoot._Ready());
  }

  [TestCase]
  public async Task ConcreteViewBuildsItsAssignedBackgroundAcrossCoverAndPop()
  {
    var manager = BuildManager();
    var view = CreateEngineeringView(); // real concrete controller, not a fake subclass
    AddBackdrop(view, new Node3D { Name = "BackdropProto" });

    manager.Push(view);

    var layer = view.GetNode<SubViewportContainer>("Background");
    Assert.True(layer.IsVisibleInTree());
    var backgroundViewport = layer.GetNode<SubViewport>("Viewport");
    Assert.True(backgroundViewport.GetChild(0).Name == "BackdropProto");

    manager.Push(new FakeView { Name = "Cover" });
    Assert.False(layer.IsVisibleInTree()); // hidden with its covered view, but retained
    Assert.True(backgroundViewport.GetChild(0).GetParent() is not null);

    manager.Pop();
    Assert.True(layer.IsVisibleInTree()); // restored on Back without re-instantiation

    manager.Pop(); // the concrete view pops: its background goes with it
    Assert.True(view.GetParent() is null);
    Assert.True(view.IsQueuedForDeletion());
    await WaitForDeferredDeletion(manager.GetTree());
    Assert.False(GodotObject.IsInstanceValid(view));
    Assert.False(GodotObject.IsInstanceValid(layer));
  }

  [TestCase]
  public void CoveredViewsCannotNavigate()
  {
    var manager = BuildManager();
    var a = new FakeView { Name = "A" };
    var b = new FakeView { Name = "B" };
    manager.Push(a);
    manager.Push(b);
    var ignored = AutoFree(new FakeView { Name = "C" }); // produced, never hosted

    a.RequestBack();
    a.RequestView(ignored);

    Assert.True(ReferenceEquals(b, manager.Current));
    Assert.True(b.IsVisibleInTree());
    Assert.Equal(3, manager.GetChildCount()); // root + a + b, nothing pushed or popped
  }

  [TestCase]
  public async Task PoppedViewsCannotNavigateAndAreInvalidAfterFrame()
  {
    var manager = BuildManager();
    var a = new FakeView { Name = "A" };
    var b = new FakeView { Name = "B" };
    manager.Push(a);
    manager.Push(b);
    manager.Pop();

    Assert.True(b.GetParent() is null);
    Assert.True(b.IsQueuedForDeletion());
    var ignored = AutoFree(new FakeView { Name = "C" }); // produced, never hosted
    b.RequestBack();
    b.RequestView(ignored);
    Assert.True(ReferenceEquals(a, manager.Current));

    await WaitForDeferredDeletion(manager.GetTree());
    Assert.False(GodotObject.IsInstanceValid(b));
  }

  [TestCase]
  public async Task ExitTreeDisconnectsNavigationUntilReentry()
  {
    await using var cleanup = new DeferredNodeCleanup();
    var manager = BuildManager();
    var a = new FakeView { Name = "A" };
    manager.Push(a);

    manager.GetParent()!.RemoveChild(manager);
    a.RequestBack();
    Assert.True(ReferenceEquals(a, manager.Current)); // disconnected on exit

    ((SceneTree)Engine.GetMainLoop()).Root.AddChild(manager);
    a.RequestBack();
    Assert.True(ReferenceEquals(manager.RootView, manager.Current)); // resubscribed on reentry
  }

  [TestCase]
  public async Task DisposalReleasesRetainedViewsAndBackgrounds()
  {
    var manager = BuildSelfFreeingManager();
    var root = (FakeView)manager.RootView;
    var backgrounded = CreateBaseView(); // authored Background/Viewport shell
    AddBackdrop(backgrounded, new Node3D { Name = "BackdropProto" });
    manager.Push(backgrounded);
    var layer = backgrounded.GetNode<SubViewportContainer>("Background");
    var tree = (SceneTree)Engine.GetMainLoop();

    manager.QueueFree();
    Assert.True(manager.IsQueuedForDeletion());
    await WaitForDeferredDeletion(tree);

    Assert.False(GodotObject.IsInstanceValid(manager));
    Assert.False(GodotObject.IsInstanceValid(backgrounded));
    Assert.False(GodotObject.IsInstanceValid(root));
    Assert.False(GodotObject.IsInstanceValid(layer));
  }

  [TestCase]
  public async Task ViewChangedFiresAfterStackAndTreeStateIsCorrect()
  {
    await using var cleanup = new DeferredNodeCleanup();
    var manager = BuildManager();
    var a = new FakeView { Name = "A" };
    var b = new FakeView { Name = "B" };
    var seen = new System.Collections.Generic.List<GeoscapeView>();
    var states = new System.Collections.Generic.List<bool>();
    manager.ViewChanged += view =>
    {
      seen.Add(view);
      states.Add(view.IsInsideTree() && view.Visible && ReferenceEquals(view, manager.Current));
    };

    manager.Push(a);
    manager.Push(b);
    manager.Pop();
    manager.Pop(); // reactivates the root

    Assert.Equal(4, seen.Count);
    Assert.True(ReferenceEquals(a, seen[0]));
    Assert.True(ReferenceEquals(b, seen[1]));
    Assert.True(ReferenceEquals(a, seen[2]));
    Assert.True(ReferenceEquals(manager.RootView, seen[3]));
    foreach (bool correct in states)
      Assert.True(correct, "ViewChanged must fire after stack and tree state are correct.");
  }

  [TestCase]
  public async Task BackgroundTracksTheScreenRegardlessOfMapCameraAndSurvivesCoveringUntilPop()
  {
    var scene = CreateGeoscapeScene(MakeStart());
    var viewport = CreateUiViewport(scene, new Vector2I(1600, 900));
    var manager = scene.GetNode<GeoscapeViewManager>("%ViewManager");
    var view = CreateBaseView(); // authored Background/Viewport shell
    AddBackdrop(view, new Node3D { Name = "BackdropProto" });
    manager.Push(view);
    await WaitForLayout(scene);
    var layer = view.GetNode<SubViewportContainer>("Background");
    var backgroundViewport = layer.GetNode<SubViewport>("Viewport");
    Node firstBackdrop = backgroundViewport.GetChild(0);
    Assert.True(layer.IsVisibleInTree());
    Assert.Equal(new Rect2(0, 0, 1600, 900), ScreenRect(layer));
    Assert.Equal(new Vector2I(1600, 900), backgroundViewport.Size);

    var camera = MapCamera(scene);
    camera.Position = new Vector2(1234, 567);
    camera.Zoom = new Vector2(2, 2);
    viewport.Size = new Vector2I(800, 600);
    camera.ForceUpdateScroll();
    await WaitForLayout(scene);
    Assert.Equal(new Rect2(0, 0, 800, 600), ScreenRect(layer));
    Assert.Equal(new Vector2I(800, 600), backgroundViewport.Size);

    manager.Push(new FakeView { Name = "Cover" });
    await WaitForLayout(scene);
    Assert.False(layer.IsVisibleInTree()); // hidden with its covered view, but retained
    Assert.True(firstBackdrop.GetParent() is not null);

    manager.Pop();
    Assert.True(layer.IsVisibleInTree()); // restored on Back without re-instantiation
    Assert.True(ReferenceEquals(firstBackdrop, backgroundViewport.GetChild(0)));

    manager.Pop(); // the backgrounded view itself pops: background goes with it
    Assert.True(view.GetParent() is null);
    await WaitForLayout(scene);
    Assert.False(GodotObject.IsInstanceValid(view));
    Assert.False(GodotObject.IsInstanceValid(firstBackdrop));
    Assert.True(MapViewport(scene).IsVisibleInTree());
  }
}
