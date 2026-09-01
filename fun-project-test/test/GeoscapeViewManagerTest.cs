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
    public event Action? Closed;
    public void RaiseClosed() => Closed?.Invoke();
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
    liveView = (FakeView)manager.GetChildren()[0];
    return manager;
  }

  [TestCase(TestName = "Open instantiates the view, sets Current, raises ViewOpened, adds as child")]
  public void OpenInstantiatesView()
  {
    var manager = BuildManager(open: false);
    GeoscapeView? firedView = null;
    Control? firedInstance = null;
    manager.ViewOpened += (v, inst) => { firedView = v; firedInstance = inst; };

    manager.Open(GeoscapeView.Units);
    var liveView = (FakeView)manager.GetChildren()[0];

    Assert.Equal(GeoscapeView.Units, manager.Current);
    Assert.Equal(1, manager.GetChildCount());
    Assert.True(liveView.IsInsideTree());
    Assert.Equal(GeoscapeView.Units, firedView);
    Assert.True(ReferenceEquals(liveView, firedInstance));
  }

  [TestCase(TestName = "View raising Closed returns to Map, frees the instance, raises ViewClosed")]
  public void ClosedFreesView()
  {
    var manager = BuildManager(out FakeView view);
    GeoscapeView? closedWith = null;
    manager.ViewClosed += v => closedWith = v;

    view.RaiseClosed();

    Assert.Equal(GeoscapeView.Map, manager.Current);
    Assert.Equal(0, manager.GetChildCount());
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
    Assert.Equal(1, manager.GetChildCount());
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
    var second = (FakeView)manager.GetChildren()[0];

    Assert.True(!ReferenceEquals(first, second));
  }

  [TestCase(TestName = "Open(Map) closes the active view")]
  public void OpenMapCloses()
  {
    var manager = BuildManager(out FakeView view);

    manager.Open(GeoscapeView.Map);

    Assert.Equal(GeoscapeView.Map, manager.Current);
    Assert.Equal(0, manager.GetChildCount());
    Assert.True(view.IsQueuedForDeletion());
  }
}
