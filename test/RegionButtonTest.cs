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

  [TestCase(TestName = "Tree entry derives the hit area from the authored Fill polygon")]
  public void TreeEntryDerivesHitAreaFromFill()
  {
    var button = AuthoredRegion(
    [
      new Vector2(0, 0), new Vector2(100, 0), new Vector2(100, 100), new Vector2(0, 100),
    ], fillOffset: new Vector2(10, 10));

    Assert.True(button._HasPoint(new Vector2(60, 60))); // effective area (10..110)
    Assert.True(button._HasPoint(new Vector2(11, 11)));
    Assert.False(button._HasPoint(new Vector2(5, 5))); // below the Fill offset
    Assert.False(button._HasPoint(new Vector2(150, 150)));
  }

  [TestCase(TestName = "Hit area matches the polygon, not the button rect")]
  public void PolygonHitAreaMatchesPolygon()
  {
    var button = AuthoredRegion(Square);

    Assert.True(button._HasPoint(new Vector2(50, 50)));
    Assert.True(button._HasPoint(new Vector2(11, 11)));
    Assert.False(button._HasPoint(new Vector2(111, 50)));
    Assert.False(button._HasPoint(new Vector2(-1, 50)));
    Assert.False(button._HasPoint(new Vector2(500, 500)));
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

  [TestCase(TestName = "An unshaped button fails loud on hit-testing")]
  public void UnshapedButtonThrowsOnHitTest()
  {
    // A button whose Fill child is missing (deleted in the editor): it stays unshaped, so
    // hit-testing is a wiring/authoring bug and fails loud.
    var button = AutoFree(new RegionButton());
    ((SceneTree)Engine.GetMainLoop()).Root.AddChild(button);

    Assert.Throws<InvalidOperationException>(() => button._HasPoint(new Vector2(0, 0)));
    Assert.Throws<InvalidOperationException>(() => button._HasPoint(new Vector2(500, 500)));
  }

  [TestCase(TestName = "MarkerAnchor falls back to the polygon centroid")]
  public void MarkerAnchorFallsBackToCentroid()
  {
    var button = AuthoredRegion(Square); // (10..110)² → centroid (60, 60)

    Assert.Equal(new Vector2(60, 60), button.MarkerAnchor);
  }

  [TestCase(TestName = "An authored Anchor child overrides the centroid")]
  public void AuthoredAnchorOverridesCentroid()
  {
    var button = AuthoredRegion(Square);
    button.AddChild(new Marker2D { Name = "Anchor", Position = new Vector2(500, 400) });

    Assert.Equal(new Vector2(500, 400), button.MarkerAnchor);
  }

  [TestCase(TestName = "An unshaped button fails loud when asked for its marker anchor")]
  public void UnshapedButtonThrowsOnMarkerAnchor()
  {
    var button = AutoFree(new RegionButton());
    ((SceneTree)Engine.GetMainLoop()).Root.AddChild(button);

    Assert.Throws<InvalidOperationException>(() => _ = button.MarkerAnchor);
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
