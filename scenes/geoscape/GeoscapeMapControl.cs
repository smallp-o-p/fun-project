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

    foreach (GeoscapeEvent active in _session.ActiveEvents)
    {
      Vector2 position = active.TargetRegionIndex.Match(
        index => _markerAnchors[index]!.Value,
        () => (Vector2)_mapSize / 2f); // map-wide marker: map center

      // Stamped from the authored scene: the marker owns its visual and extent (Size);
      // the map only centers it on the anchor. Added after the authored regions → drawn
      // on top → wins overlap.
      var marker = (RegionButton)EventMarkerScene.Instantiate();
      marker.Position = position - marker.Size / 2f;
      AddChild(marker);
      _markerNodes.Add(marker);
      marker.Pressed += () => EmitSignal(SignalName.EventClicked, new GeoscapeEventAdapter { Event = active });
    }
  }
}
