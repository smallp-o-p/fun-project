using Godot;
using System;

// Button whose hit area is an arbitrary 2D polygon instead of its rectangle: the semantics
// of a Button, mapped onto the shape. Overriding _HasPoint routes Godot's GUI hit-testing,
// hover, and click delivery through the real shape, and layered siblings get standard
// topmost-first semantics — a marker button above a region wins without manual arbitration.
// MouseFilter.Pass lets a parent gesture layer keep seeing presses — none exists in the
// first pass (zoom/pan were removed), kept so gestures can return without touching this
// class. Draws nothing for any button state; visuals are authored (Polygon2D "Fill" child +
// Line2D outline) or generated as children. Circle markers bake a many-sided polygon — one
// shape representation, no circle mode.
//
// Shaped at construction so an unshaped button cannot exist. The parameterless constructor
// exists only because Godot instantiates script classes for editor authoring and scene
// deserialization — such buttons are unshaped until GeoscapeMapControl.Setup derives their
// hit area from the authored Fill polygon, which happens in the same frame, before any
// input can arrive. Hit-testing an unshaped button is a wiring bug and throws.
[GlobalClass]
public sealed partial class RegionButton : Button
{
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

  public RegionButton(Vector2[] polygon) : this()
  {
    _polygon = polygon;
  }

  // The authored-node bridge: a scene-deserialized button gets its hit area assigned by
  // GeoscapeMapControl.Setup, derived from the authored Fill polygon.
  public void SetPolygonHitArea(Vector2[] points) => _polygon = points;

  public override bool _HasPoint(Vector2 point)
  {
    if (_polygon.Length == 0)
      throw new InvalidOperationException(
        "RegionButton has no hit shape; construct it with a polygon, or let GeoscapeMapControl.Setup assign one from the authored Fill.");
    return Geometry2D.IsPointInPolygon(point, _polygon);
  }
}
