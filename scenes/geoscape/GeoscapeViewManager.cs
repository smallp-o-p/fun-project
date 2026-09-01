using Godot;
using System;

public enum GeoscapeView
{
  Map,
  Units,
}

// Views implement this so the manager can own lifecycle without knowing view contents.
public interface IGeoscapeView
{
  event Action? Closed;
}

// Owns which main geoscape view is active and the on-demand view lifecycle (instantiate on
// open, free on close). Dumb switching only — no session, no GameState: data presentation
// is wired by GeoscapeScene through ViewOpened. One main view at a time; modals (the
// resolution dialog) live elsewhere and are unaffected.
public sealed partial class GeoscapeViewManager : Node
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
    AddChild(instance);
    if (instance is IGeoscapeView closable)
      closable.Closed += Close;

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

    if (view is IGeoscapeView closable)
      closable.Closed -= Close;
    RemoveChild(view); // detach now: closed views must not linger to frame end
    view.QueueFree();

    ViewClosed?.Invoke(closing);
  }
}
