using Godot;
using System;

// Invisible button shaped by its authored Polygon2D "Fill" child.
[Tool]
[GlobalClass]
public sealed partial class RegionButton : Button
{
  public RegionButton()
  {
    Flat = true;
    MouseFilter = MouseFilterEnum.Pass;
    FocusMode = FocusModeEnum.None;
    var empty = new StyleBoxEmpty();
    string[] states = ["normal", "hover", "pressed", "hover_pressed", "disabled"];
    foreach (var state in states)
      AddThemeStyleboxOverride(state, empty);
  }

  public override void _Ready()
  {
    if (Engine.IsEditorHint() || GetNodeOrNull<Polygon2D>("Fill") is not { } fill)
      return;

    var baseColor = fill.Color;
    MouseEntered += () => fill.Color = baseColor.Lightened(0.25f);
    MouseExited += () => fill.Color = baseColor;
  }

  public override bool _HasPoint(Vector2 point)
  {
    var fill = Fill;
    return fill is not null && Geometry2D.IsPointInPolygon(
      fill.Transform.AffineInverse() * point, fill.Polygon);
  }

  // An explicit Anchor supports concave regions; otherwise use the vertex average.
  public Vector2 MarkerAnchor
  {
    get
    {
      if (GetNodeOrNull<Marker2D>("Anchor") is { } anchor)
        return anchor.Position;
      if (Fill is not { } fill)
        return Vector2.Zero;

      var points = fill.Polygon;
      var sum = Vector2.Zero;
      foreach (var point in points)
        sum += point;
      return fill.Transform * (sum / points.Length);
    }
  }

  private Polygon2D? Fill
  {
    get
    {
      var fill = GetNodeOrNull<Polygon2D>("Fill");
      if (fill is not null && fill.Polygon.Length > 0)
        return fill;
      if (Engine.IsEditorHint())
        return null;

      throw new InvalidOperationException("RegionButton needs a Polygon2D Fill child with a nonempty polygon.");
    }
  }
}
