using Godot;
using System;

// Button whose hit area is an arbitrary 2D polygon instead of its rectangle: the semantics
// of a Button, mapped onto the shape. Overriding _HasPoint routes Godot's GUI hit-testing,
// hover, and click delivery through the real shape, and layered siblings get standard
// topmost-first semantics — a marker button above a region wins without manual arbitration.
// MouseFilter.Pass lets a parent gesture layer keep seeing presses — none exists in the
// first pass (zoom/pan were removed), kept so gestures can return without touching this
// class. Draws nothing for any button state; visuals are authored as children (Polygon2D
// "Fill" + Line2D outline) — regions stamp the RegionButton scene, event markers stamp the
// GeoscapeEventMarker scene. Circle markers bake a many-sided polygon — one shape
// representation, no circle mode.
//
// The authored Fill is the single shape source: entering the tree (scene load) derives the
// hit area from its polygon and wires hover lightening against its color, so every
// RegionButton — authored region or stamped marker — self-configures. A button without a
// Fill child (deleted in the editor) stays unshaped; hit-testing or asking for its marker
// anchor is then a wiring/authoring bug and fails loud. The parameterless constructor
// exists only because Godot instantiates script classes for editor authoring and scene
// deserialization.
[GlobalClass]
public sealed partial class RegionButton : Button
{
  private Polygon2D? _fill;
  private Color _baseFill;
  private Vector2[] _polygon = [];

  public RegionButton()
  {
    Flat = true;
    MouseFilter = MouseFilterEnum.Pass;
    FocusMode = FocusModeEnum.None;
    AddThemeStyleboxOverride("normal", new StyleBoxEmpty());
    AddThemeStyleboxOverride("hover", new StyleBoxEmpty());
    AddThemeStyleboxOverride("pressed", new StyleBoxEmpty());
    AddThemeStyleboxOverride("hover_pressed", new StyleBoxEmpty());
    AddThemeStyleboxOverride("disabled", new StyleBoxEmpty());
  }

  // Tree entry stands in for scene load: the authored children are present by now.
  public override void _Ready()
  {
    _fill = GetNodeOrNull<Polygon2D>("Fill");
    if (_fill is null)
      return; // unshaped: Shape fails loud on first use

    _baseFill = _fill.Color;
    _polygon = LocalPolygon(_fill);
    MouseEntered += () => _fill.Color = _baseFill.Lightened(0.25f);
    MouseExited += () => _fill.Color = _baseFill;
  }

  // Where targeted-event markers sit: an authored Marker2D "Anchor" child when present
  // (concave shapes can contain their own vertex average), else the hit polygon centroid.
  public Vector2 MarkerAnchor => GetNodeOrNull<Marker2D>("Anchor")?.Position ?? Centroid(Shape);

  public override bool _HasPoint(Vector2 point)
  {
    return Geometry2D.IsPointInPolygon(point, Shape);
  }

  private Vector2[] Shape => _polygon.Length > 0
    ? _polygon
    : throw new InvalidOperationException(
      "RegionButton has no hit shape; it needs an authored Polygon2D Fill child (was it deleted?).");

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
}
