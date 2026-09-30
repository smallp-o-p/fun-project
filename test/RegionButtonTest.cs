#nullable disable warnings
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;
using System;

[TestSuite]
[RequireGodotRuntime]
public class RegionButtonTest
{
  // Mirrors authored scenes; tree entry wires hover and AutoFree cleans up the nodes.
  private static RegionButton AuthoredRegion(Vector2[] polygon, Vector2 fillOffset = default, Color? color = null)
  {
    var button = AutoFree(new RegionButton());
    button.AddChild(new Polygon2D
    {
      Name = "Fill",
      Polygon = polygon,
      Position = fillOffset,
      Color = color ?? Colors.White,
    });
    ((SceneTree)Engine.GetMainLoop()).Root.AddChild(button);
    return button;
  }

  private static readonly Vector2[] Square =
  [
    new Vector2(10, 10), new Vector2(110, 10), new Vector2(110, 110), new Vector2(10, 110),
  ];

  // Circle markers bake a many-sided polygon; this mirrors the authored marker scene's Fill.
  private static Vector2[] CirclePolygon(float radius, Vector2 center, int segments = 24)
  {
    var points = new Vector2[segments];
    for (int i = 0; i < segments; i++)
    {
      float angle = i * Mathf.Tau / segments;
      points[i] = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
    }

    return points;
  }

  [TestCase(false, 50, 50, true, TestName = "Square fill hits the polygon interior (50,50)")]
  [TestCase(false, 11, 11, true, TestName = "Square fill hits near the polygon corner (11,11)")]
  [TestCase(false, 111, 50, false, TestName = "Square fill rejects (111,50) beyond the polygon's right edge")]
  [TestCase(false, -1, 50, false, TestName = "Square fill rejects (-1,50) left of the polygon")]
  [TestCase(false, 500, 500, false, TestName = "Square fill rejects (500,500) far outside the polygon")]
  [TestCase(true, 60, 60, true, TestName = "Offset fill hits the shifted interior (60,60)")]
  [TestCase(true, 11, 11, true, TestName = "Offset fill hits just inside the shifted edge (11,11)")]
  [TestCase(true, 5, 5, false, TestName = "Offset fill rejects (5,5) below the Fill offset")]
  [TestCase(true, 150, 150, false, TestName = "Offset fill rejects (150,150) beyond the shifted polygon")]
  public void FillPolygonDefinesTheHitArea(bool offsetFill, int x, int y, bool expected)
  {
    var button = offsetFill
      ? AuthoredRegion(
      [
        new Vector2(0, 0), new Vector2(100, 0), new Vector2(100, 100), new Vector2(0, 100),
      ], fillOffset: new Vector2(10, 10))
      : AuthoredRegion(Square);

    Assert.Equal(expected, button._HasPoint(new Vector2(x, y)), $"point=({x},{y}), offsetFill={offsetFill}");
  }

  [TestCase]
  public void HitAreaTracksPolygonEdits()
  {
    var button = AuthoredRegion(Square);
    button.GetNode<Polygon2D>("Fill").Polygon =
    [
      new Vector2(200, 200), new Vector2(300, 200), new Vector2(300, 300), new Vector2(200, 300),
    ];

    Assert.False(button._HasPoint(new Vector2(50, 50)));
    Assert.True(button._HasPoint(new Vector2(250, 250)));
  }

  [TestCase]
  public void HitAreaTracksFillTransformEdits()
  {
    var button = AuthoredRegion(Square);
    var fill = button.GetNode<Polygon2D>("Fill");
    fill.Position = new Vector2(300, 0);
    fill.Rotation = Mathf.Pi / 2;
    fill.Scale = new Vector2(2, 1);

    // Rotated/scaled square occupies x=190..290, y=20..220.
    Assert.True(button._HasPoint(new Vector2(240, 120)));
    Assert.False(button._HasPoint(new Vector2(180, 120)));
    Assert.False(button._HasPoint(new Vector2(50, 50)));
  }

  [TestCase]
  public void MarkerAnchorTracksFillEdits()
  {
    var button = AuthoredRegion(Square);
    var fill = button.GetNode<Polygon2D>("Fill");
    fill.Polygon =
    [
      new Vector2(0, 0), new Vector2(20, 0), new Vector2(20, 40), new Vector2(0, 40),
    ];
    fill.Position = new Vector2(100, 200);
    fill.Scale = new Vector2(2, 3);

    Assert.Equal(new Vector2(120, 260), button.MarkerAnchor);
  }

  [TestCase(TestName = "Concave polygons exclude the notch")]
  public void ConcavePolygonExcludesNotch()
  {
    // L-shape: the (80, 80) corner is carved out.
    var button = AuthoredRegion(
    [
      new Vector2(0, 0), new Vector2(100, 0), new Vector2(100, 40),
      new Vector2(40, 40), new Vector2(40, 100), new Vector2(0, 100),
    ]);

    Assert.True(button._HasPoint(new Vector2(20, 20)));
    Assert.False(button._HasPoint(new Vector2(80, 80)));
  }

  [TestCase(TestName = "Baked circle fills hit inside the radius, not outside")]
  public void CirclePolygonHitsWithinRadius()
  {
    var button = AuthoredRegion(CirclePolygon(28f, new Vector2(28, 28)));

    Assert.True(button._HasPoint(new Vector2(28, 28)));
    Assert.True(button._HasPoint(new Vector2(28 + 27, 28))); // inside the 24-gon's inset
    Assert.False(button._HasPoint(new Vector2(28 + 29, 28)));
    Assert.False(button._HasPoint(new Vector2(0, 0))); // rect corner, outside the shape
  }

  [TestCase(TestName = "An unshaped button fails loud on hit-testing and marker anchors")]
  public void UnshapedButtonFailsLoud()
  {
    // A button whose Fill child is missing (deleted in the editor): it stays unshaped, so
    // hit-testing and marker anchoring are wiring/authoring bugs and fail loud.
    var button = AutoFree(new RegionButton());
    ((SceneTree)Engine.GetMainLoop()).Root.AddChild(button);

    Assert.Throws<InvalidOperationException>(() => button._HasPoint(new Vector2(0, 0)));
    Assert.Throws<InvalidOperationException>(() => button._HasPoint(new Vector2(500, 500)));
    Assert.Throws<InvalidOperationException>(() => _ = button.MarkerAnchor);
  }

  [TestCase(false, TestName = "MarkerAnchor falls back to the polygon centroid")]
  [TestCase(true, TestName = "An authored Anchor child overrides the centroid")]
  public void MarkerAnchorUsesTheAuthoredAnchorWhenPresent(bool authoredAnchor)
  {
    var button = AuthoredRegion(Square); // (10..110)² → centroid (60, 60)
    if (authoredAnchor)
      button.AddChild(new Marker2D { Name = "Anchor", Position = new Vector2(500, 400) });

    Assert.Equal(authoredAnchor ? new Vector2(500, 400) : new Vector2(60, 60), button.MarkerAnchor);
  }

  [TestCase(TestName = "Hover lightens the Fill; mouse exit restores it")]
  public void HoverLightensFill()
  {
    // Authored fills are colored before load (white is Polygon2D's default and cannot
    // lighten), so the color is part of the authored shape, not a runtime recolor.
    var button = AuthoredRegion(Square, color: new Color(0.3f, 0.55f, 0.3f));
    var fill = button.GetNode<Polygon2D>("Fill");
    Color baseColor = fill.Color;

    button.EmitSignal(Control.SignalName.MouseEntered);
    Assert.Equal(baseColor.Lightened(0.25f), fill.Color);

    button.EmitSignal(Control.SignalName.MouseExited);
    Assert.Equal(baseColor, fill.Color);
  }
}
