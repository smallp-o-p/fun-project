using Godot;
using GdUnit4;
using static GdUnit4.Assertions;
using System;

[TestSuite]
[RequireGodotRuntime]
public class RegionButtonTest
{
  // Circle markers bake a many-sided polygon; this mirrors GeoscapeMapControl's bake.
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

  [TestCase(TestName = "Polygon hit area matches the polygon, not the rect")]
  public void PolygonHitAreaMatchesPolygon()
  {
    var button = AutoFree(new RegionButton(
    [
      new Vector2(0, 0), new Vector2(100, 0), new Vector2(100, 100), new Vector2(0, 100),
    ]));

    Assert.True(button._HasPoint(new Vector2(50, 50)));
    Assert.True(button._HasPoint(new Vector2(1, 1)));
    Assert.False(button._HasPoint(new Vector2(101, 50)));
    Assert.False(button._HasPoint(new Vector2(-1, 50)));
    Assert.False(button._HasPoint(new Vector2(500, 500)));
  }

  [TestCase(TestName = "Concave polygons exclude the notch")]
  public void ConcavePolygonExcludesNotch()
  {
    // L-shape: the (80, 80) corner is carved out.
    var button = AutoFree(new RegionButton(
    [
      new Vector2(0, 0), new Vector2(100, 0), new Vector2(100, 40),
      new Vector2(40, 40), new Vector2(40, 100), new Vector2(0, 100),
    ]));

    Assert.True(button._HasPoint(new Vector2(20, 20)));
    Assert.False(button._HasPoint(new Vector2(80, 80)));
  }

  [TestCase(TestName = "Baked circle polygons hit inside the radius, not outside")]
  public void CirclePolygonHitsWithinRadius()
  {
    var button = AutoFree(new RegionButton(CirclePolygon(28f, new Vector2(28, 28))));

    Assert.True(button._HasPoint(new Vector2(28, 28)));
    Assert.True(button._HasPoint(new Vector2(28 + 27, 28))); // inside the 24-gon's inset
    Assert.False(button._HasPoint(new Vector2(28 + 29, 28)));
    Assert.False(button._HasPoint(new Vector2(0, 0))); // rect corner, outside the shape
  }

  [TestCase(TestName = "An unshaped button fails loud on hit-testing")]
  public void UnshapedButtonThrowsOnHitTest()
  {
    // Only the editor/scene-deserialization path can produce these; Setup assigns the hit
    // area before any input can arrive, so reaching hit-testing unshaped is a wiring bug.
    var button = AutoFree(new RegionButton());

    Assert.Throws<InvalidOperationException>(() => button._HasPoint(new Vector2(0, 0)));
    Assert.Throws<InvalidOperationException>(() => button._HasPoint(new Vector2(500, 500)));
  }
}
