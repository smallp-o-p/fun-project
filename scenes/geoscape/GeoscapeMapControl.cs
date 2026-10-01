using FunProject.Strategic;
using Godot;
using System;
using System.Collections.Generic;

// Draws the geoscape: region visuals are AUTHORED in the Godot editor as instances of the
// RegionButton scene under this control (each with a Polygon2D "Fill" child and Line2D
// outline — no collision shapes; buttons derive their hit area and hover from the Fill
// when they enter the tree). Each region node's NAME must match a RegionData.Name in
// GeoscapeMapData.Regions — the session produces the name-to-index binding from the
// authoritative array at construction, so array order never needs re-tagging. This script
// wires those authored buttons to session data (click routing, marker anchors) and stamps
// active-event markers from the authored GeoscapeEventMarker scene (EventMarkerScene
// export) layered above the regions — GUI hit-testing offers each event to the topmost
// control, so a marker wins over the region beneath it without arbitration. Session events
// are routed by the composition root (GeoscapeScene), which calls RefreshEvents; never
// mutates the session; emits Godot signals for the composition root to route.
public sealed partial class GeoscapeMapControl : Control
{
  private GeoscapeSession _session = null!;
  private Vector2I _mapSize;
  private Vector2?[] _markerAnchors = [];
  private readonly List<RegionButton> _markerNodes = [];

  [Export] public PackedScene? EventMarkerScene { get; set; }

  [Signal] public delegate void RegionClickedEventHandler(RegionData region);
  [Signal] public delegate void EventClickedEventHandler(GeoscapeEventAdapter adapter);

  public void Setup(GeoscapeSession session, Vector2I mapSize)
  {
    _ = EventMarkerScene ?? throw new InvalidOperationException(
      "GeoscapeMapControl requires EventMarkerScene; assign a PackedScene in the inspector.");
    _session = session;
    _mapSize = mapSize;

    _markerAnchors = new Vector2?[session.Regions.Count];
    foreach (Node child in GetChildren())
    {
      if (child is not RegionButton button)
        continue;

      int index = session.IndexOfRegion(button.Name);
      if (index < 0)
        throw new InvalidOperationException(
          $"Authored region node '{button.Name}' has no matching RegionData in GeoscapeMapData.Regions.");

      _markerAnchors[index] = button.MarkerAnchor;

      RegionData data = session.Regions[index];
      button.Pressed += () => EmitSignal(SignalName.RegionClicked, data);
    }

    foreach (Vector2? anchor in _markerAnchors)
      if (anchor is null)
        throw new InvalidOperationException(
          "Every region in GeoscapeMapData.Regions needs an authored node under the map control.");
  }

  public void RefreshEvents()
  {
    // Detach before queueing the deferred free: replacement markers must not coexist with
    // stale ones (still hit-testable and drawn) until the end of frame.
    foreach (Node marker in _markerNodes)
    {
      RemoveChild(marker);
      marker.QueueFree();
    }
    _markerNodes.Clear();

    List<Vector2> anchors = [];
    foreach (GeoscapeEvent active in _session.ActiveEvents)
    {
      anchors.Add(active.TargetRegionIndex.Match(
        index => _markerAnchors[index]!.Value,
        () => (Vector2)_mapSize / 2f)); // map-wide marker: map center

      // Keep the authored visual, hit shape, and signal for every event. Placement uses
      // the full extent, so no later sibling can intercept another marker's clicks.
      var marker = (RegionButton)EventMarkerScene.Instantiate();
      AddChild(marker);
      _markerNodes.Add(marker);
      marker.Pressed += () => EmitSignal(SignalName.EventClicked, new GeoscapeEventAdapter { Event = active });
    }

    if (_markerNodes.Count > 0 && !TryPlaceMarkersNearAnchors(anchors))
      PackMarkers();
  }

  private bool TryPlaceMarkersNearAnchors(List<Vector2> anchors)
  {
    List<Rect2> placed = [];
    for (int index = 0; index < _markerNodes.Count; index++)
    {
      RegionButton marker = _markerNodes[index];
      Vector2 limit = (Vector2)_mapSize - marker.Size;
      if (limit.X < 0 || limit.Y < 0)
        return false;

      Vector2 desired = anchors[index] - marker.Size / 2f;
      Vector2 preferred = desired.Clamp(Vector2.Zero, limit);
      var rect = new Rect2(preferred, marker.Size);
      if (IsFree(rect, placed))
      {
        marker.Position = preferred;
        placed.Add(rect);
        continue;
      }

      // The closest free rectangle touches an obstacle edge on each displaced axis.
      // Test those coordinates together, preserving insertion order to break ties.
      // Bound the search for unusually large event sets; the grid below always fits.
      if (placed.Count >= 64)
        return false;
      List<float> xs = [preferred.X, 0, limit.X];
      List<float> ys = [preferred.Y, 0, limit.Y];
      foreach (Rect2 occupied in placed)
      {
        xs.Add(Mathf.Clamp(occupied.Position.X - marker.Size.X, 0, limit.X));
        xs.Add(Mathf.Clamp(occupied.End.X, 0, limit.X));
        ys.Add(Mathf.Clamp(occupied.Position.Y - marker.Size.Y, 0, limit.Y));
        ys.Add(Mathf.Clamp(occupied.End.Y, 0, limit.Y));
      }

      float nearestDistance = float.PositiveInfinity;
      foreach (float x in xs)
        foreach (float y in ys)
        {
          var candidate = new Rect2(new Vector2(x, y), marker.Size);
          float distance = candidate.Position.DistanceSquaredTo(desired);
          if (distance < nearestDistance && IsFree(candidate, placed))
          {
            rect = candidate;
            nearestDistance = distance;
          }
        }
      if (float.IsPositiveInfinity(nearestDistance))
        return false;

      marker.Position = rect.Position;
      placed.Add(rect);
    }
    return true;
  }

  private static bool IsFree(Rect2 candidate, List<Rect2> placed)
  {
    foreach (Rect2 occupied in placed)
      if (candidate.Intersects(occupied))
        return false;
    return true;
  }

  private void PackMarkers()
  {
    // Greedy anchor placement can exhaust the available space. Retry in uniform cells,
    // choosing the grid with the largest shared scale; every event keeps its own button.
    Vector2 largest = Vector2.Zero;
    foreach (RegionButton marker in _markerNodes)
      largest = new Vector2(Mathf.Max(largest.X, marker.Size.X), Mathf.Max(largest.Y, marker.Size.Y));

    int columns = 1;
    float scale = 0;
    for (int candidate = 1; candidate <= _markerNodes.Count; candidate++)
    {
      int rows = (_markerNodes.Count + candidate - 1) / candidate;
      float fit = Mathf.Min(1f, Mathf.Min(_mapSize.X / (candidate * largest.X), _mapSize.Y / (rows * largest.Y)));
      if (fit > scale)
      {
        columns = candidate;
        scale = fit;
      }
    }

    // A small inset prevents floating-point contact at scaled cell boundaries.
    if (scale < 1f)
      scale *= 0.98f;
    int rowCount = (_markerNodes.Count + columns - 1) / columns;
    var cellSize = new Vector2((float)_mapSize.X / columns, (float)_mapSize.Y / rowCount);
    for (int index = 0; index < _markerNodes.Count; index++)
    {
      RegionButton marker = _markerNodes[index];
      marker.Scale = Vector2.One * scale;
      marker.Position = new Vector2(index % columns, index / columns) * cellSize
        + (cellSize - marker.Size * scale) / 2f;
    }
  }
}
