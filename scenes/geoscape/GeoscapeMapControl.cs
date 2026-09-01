using FunProject.Strategic;
using Godot;
using System;
using System.Collections.Generic;

// Draws the geoscape: region visuals are AUTHORED in the Godot editor as RegionButton
// children of this control (each with a Polygon2D "Fill" child and Line2D outline — no
// collision shapes; the button's hit area derives from the Fill polygon). Each region
// node's NAME must match a RegionData.Name in GeoscapeMapData.Regions — the session
// produces the name-to-index binding from the authoritative array at construction, so
// array order never needs re-tagging. This script wires those authored buttons (hover,
// clicks) and spawns active-event markers at runtime as RegionButtons layered above the
// regions — GUI hit-testing offers each event to the topmost control, so a marker wins
// over the region beneath it without arbitration. Buttons own their clicks and hover
// outright. Session events are routed by the composition root (GeoscapeScene), which calls
// RefreshEvents; never mutates the session; emits Godot signals for the composition root
// to route.
public sealed partial class GeoscapeMapControl : Control
{
  private const float MarkerRadiusWorld = 28f;
  private const int MarkerCircleSegments = 24;

  private GeoscapeSession _session = null!;
  private Vector2I _mapSize;
  private Vector2[]?[] _visuals = [];
  private Vector2[] _markerAnchors = [];
  private readonly List<RegionButton> _markerNodes = [];

  [Signal] public delegate void RegionClickedEventHandler(RegionData region);
  [Signal] public delegate void EventClickedEventHandler(GeoscapeEventAdapter adapter);

  public void Setup(GeoscapeSession session, Vector2I mapSize)
  {
    _session = session;
    _mapSize = mapSize;

    _visuals = new Vector2[session.Regions.Count][];
    _markerAnchors = new Vector2[session.Regions.Count];
    foreach (Node child in GetChildren())
    {
      if (child is not RegionButton button)
        continue;

      int index = session.IndexOfRegion(button.Name);
      if (index < 0)
        throw new InvalidOperationException(
          $"Authored region node '{button.Name}' has no matching RegionData in GeoscapeMapData.Regions.");

      var fill = button.GetNode<Polygon2D>("Fill");
      Vector2[] points = LocalPolygon(fill);
      _visuals[index] = points;
      button.SetPolygonHitArea(points);

      // Where targeted-event markers sit: an authored Marker2D "Anchor" child when present
      // (concave shapes can contain their own vertex average), else the polygon centroid.
      _markerAnchors[index] = button.GetNodeOrNull<Marker2D>("Anchor")?.Position ?? Centroid(points);

      RegionData data = session.Regions[index];
      Color baseColor = fill.Color;
      button.MouseEntered += () => fill.Color = baseColor.Lightened(0.25f);
      button.MouseExited += () => fill.Color = baseColor;
      button.Pressed += () => EmitSignal(SignalName.RegionClicked, data);
    }

    foreach (Vector2[]? points in _visuals)
      if (points is null)
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

    foreach (GeoscapeEvent active in _session.ActiveEvents)
    {
      Vector2 position = active.TargetRegionIndex.Match(
        index => _markerAnchors[index],
        () => (Vector2)_mapSize / 2f); // map-wide marker: map center
      _markerNodes.Add(BuildMarkerButton(active, position));
    }
  }

  private RegionButton BuildMarkerButton(GeoscapeEvent active, Vector2 position)
  {
    // One baked polygon serves as both hit shape and visual. Origin at its corner so the
    // rect covers the shape; centered inside. Added after the authored regions → drawn on
    // top → wins overlap.
    Vector2 center = new(MarkerRadiusWorld, MarkerRadiusWorld);
    Vector2[] circle = CirclePoints(MarkerRadiusWorld, MarkerCircleSegments, center);
    var button = new RegionButton(circle)
    {
      Position = position - center,
      Size = new Vector2(MarkerRadiusWorld * 2f, MarkerRadiusWorld * 2f),
    };
    AddChild(button);

    button.AddChild(new Polygon2D
    {
      Polygon = circle,
      Color = Colors.White with { A = 0.85f },
    });

    button.Pressed += () => EmitSignal(SignalName.EventClicked, new GeoscapeEventAdapter { Event = active });
    return button;
  }

  // The Fill's polygon in this button's local space: its own transform applied to each
  // point (position, but also any authored rotation/scale).
  private static Vector2[] LocalPolygon(Polygon2D fill)
  {
    var points = new Vector2[fill.Polygon.Length];
    for (int i = 0; i < points.Length; i++)
      points[i] = fill.Transform * fill.Polygon[i];
    return points;
  }

  private static Vector2 Centroid(Vector2[] points)
  {
    var sum = Vector2.Zero;
    foreach (Vector2 point in points)
      sum += point;

    return sum / points.Length;
  }

  private static Vector2[] CirclePoints(float radius, int segments, Vector2 center)
  {
    var points = new Vector2[segments];
    for (int i = 0; i < segments; i++)
    {
      float angle = i * Mathf.Tau / segments;
      points[i] = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
    }

    return points;
  }
}
