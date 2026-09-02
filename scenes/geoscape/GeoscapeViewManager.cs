using Godot;
using System;

public enum GeoscapeView
{
  Map,
  Units,
  Unit,
}

// Views receive the ability to request their own closing (armed by the manager on open).
// The manager is the sole performer of a close — it owns state, events, and freeing — so
// views ask; they never raise a broadcast and never free themselves.
public interface IGeoscapeView
{
  void ArmClose(Action requestClose);
}

// Views that want a full-screen backdrop behind their UI implement this. The manager owns
// the viewport infrastructure and the backdrop lifecycle: it instantiates BackdropScene
// into its SubViewport on open, frees it on close. The scene can be 3D or 2D — a
// SubViewport renders either — and never receives input.
public interface IGeoscapeViewBackdrop
{
  PackedScene? BackdropScene { get; }
}

// Owns which main geoscape view is active and the on-demand view lifecycle (instantiate on
// open, free on close). This node IS the overlay layer: a full-rect Control parented under
// GeoscapeScene above the map, so views anchor to the scene's rect (not the raw viewport)
// and genuinely overlay the geoscape while open. The layer itself passes mouse input
// through when no view is hosted (authored mouse_filter = ignore). Dumb switching only —
// no session, no GameState: data presentation is wired by GeoscapeScene through ViewOpened.
// One main view at a time; modals (the resolution dialog) live elsewhere and are unaffected.
// It also hosts the view backdrop: one full-rect SubViewport below every view instance,
// filled from the view's IGeoscapeViewBackdrop declaration on open, emptied on close.
public sealed partial class GeoscapeViewManager : Control
{
  [Export] public PackedScene? UnitsView { get; set; }
  [Export] public PackedScene? UnitView { get; set; }

  private Control? _activeView;
  private SubViewportContainer _backdropLayer = null!;
  private SubViewport _backdropViewport = null!;

  public GeoscapeView Current { get; private set; } = GeoscapeView.Map;

  public event Action<GeoscapeView, Control>? ViewOpened;
  public event Action<GeoscapeView>? ViewClosed;

  // Code-built like GeoscapeCameraRig: one full-rect viewport added first, so it draws
  // behind every view instance. Stretch resizes the viewport to the layer (full-bleed
  // backdrops); the viewport's default update mode (when visible) means a hidden layer
  // renders nothing while the map is shown.
  public override void _Ready()
  {
    _backdropLayer = new SubViewportContainer
    {
      Name = "Backdrop",
      Stretch = true,
      MouseFilter = MouseFilterEnum.Ignore, // backdrops are decorative; they never take input
      Visible = false,
    };
    _backdropLayer.SetAnchorsPreset(LayoutPreset.FullRect);
    _backdropViewport = new SubViewport
    {
      Name = "Viewport",
      OwnWorld3D = true, // 3D backdrops never pick up an outer world
    };
    _backdropLayer.AddChild(_backdropViewport);
    AddChild(_backdropLayer);
  }

  public void Open(GeoscapeView view)
  {
    if (view == Current)
      return;

    if (view == GeoscapeView.Map)
    {
      Close();
      return;
    }

    Close(); // one main view at a time

    PackedScene scene = view switch
    {
      GeoscapeView.Units => UnitsView ?? throw new InvalidOperationException(
        "GeoscapeViewManager requires UnitsView; assign a PackedScene in the inspector."),
      GeoscapeView.Unit => UnitView ?? throw new InvalidOperationException(
        "GeoscapeViewManager requires UnitView; assign a PackedScene in the inspector."),
      _ => throw new ArgumentOutOfRangeException(nameof(view), view, null),
    };

    var instance = (Control)scene.Instantiate();
    _activeView = instance;
    Current = view;
    AddChild(instance); // the view anchors to this layer's full-rect, i.e. the scene
    if (instance is IGeoscapeView armable)
      armable.ArmClose(Close);
    if (instance is IGeoscapeViewBackdrop { BackdropScene: { } backdrop })
    {
      _backdropViewport.AddChild(backdrop.Instantiate());
      _backdropLayer.Visible = true;
    }

    ViewOpened?.Invoke(view, instance);
  }

  public void Close()
  {
    if (_activeView is null)
      return;

    GeoscapeView closing = Current;
    Current = GeoscapeView.Map;
    Control view = _activeView;
    _activeView = null;

    RemoveChild(view); // detach now: closed views must not linger to frame end
    view.QueueFree();
    ClearBackdrop();

    ViewClosed?.Invoke(closing);
  }

  private void ClearBackdrop()
  {
    foreach (Node child in _backdropViewport.GetChildren())
    {
      _backdropViewport.RemoveChild(child); // detach now: mirrors the view free
      child.QueueFree();
    }
    _backdropLayer.Visible = false;
  }
}
