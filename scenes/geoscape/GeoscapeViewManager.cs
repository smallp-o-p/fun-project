using Godot;
using System;

public enum GeoscapeView
{
  Map,
  Units,
}

// Views receive the ability to request their own closing (armed by the manager on open).
// The manager is the sole performer of a close — it owns state, events, and freeing — so
// views ask; they never raise a broadcast and never free themselves.
public interface IGeoscapeView
{
  void ArmClose(Action requestClose);
}

// Owns which main geoscape view is active and the on-demand view lifecycle (instantiate on
// open, free on close). This node IS the overlay layer: a full-rect Control parented under
// GeoscapeScene above the map, so views anchor to the scene's rect (not the raw viewport)
// and genuinely overlay the geoscape while open. The layer itself passes mouse input
// through when no view is hosted (authored mouse_filter = ignore). Dumb switching only —
// no session, no GameState: data presentation is wired by GeoscapeScene through ViewOpened.
// One main view at a time; modals (the resolution dialog) live elsewhere and are unaffected.
public sealed partial class GeoscapeViewManager : Control
{
  [Export] public PackedScene? UnitsView { get; set; }

  private Control? _activeView;

  public GeoscapeView Current { get; private set; } = GeoscapeView.Map;

  public event Action<GeoscapeView, Control>? ViewOpened;
  public event Action<GeoscapeView>? ViewClosed;

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
      _ => throw new ArgumentOutOfRangeException(nameof(view), view, null),
    };

    var instance = (Control)scene.Instantiate();
    _activeView = instance;
    Current = view;
    AddChild(instance); // the view anchors to this layer's full-rect, i.e. the scene
    if (instance is IGeoscapeView armable)
      armable.ArmClose(Close);

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

    ViewClosed?.Invoke(closing);
  }
}
