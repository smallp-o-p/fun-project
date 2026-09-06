using FunProject.Strategic;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;
using System;

[TestSuite]
[RequireGodotRuntime]
public class GeoscapeMapControlTest
{
  // Builds the authored shape the editor would produce: a RegionButton child of the map,
  // named after its RegionData, with a Polygon2D "Fill" child carrying the polygon (and an
  // optional offset). The map enters the tree — tree entry stands in for scene load, so
  // the buttons self-configure (_Ready derives hit areas and wires hover). Only the map
  // needs AutoFree — children free transitively.
  private static GeoscapeMapControl BuildMap(RegionButton[] buttons)
  {
    var map = AutoFree(new GeoscapeMapControl { EventMarkerScene = PackMarkerProto() });
    foreach (RegionButton button in buttons)
      map.AddChild(button);
    ((SceneTree)Engine.GetMainLoop()).Root.AddChild(map);
    return map;
  }

  private static RegionButton RegionButtonNamed(string name, Vector2[] polygon, Vector2 fillOffset = default)
  {
    var button = new RegionButton { Name = name };
    button.AddChild(new Polygon2D { Name = "Fill", Polygon = polygon, Position = fillOffset });
    return button;
  }

  // Stands in for the authored GeoscapeEventMarker.tscn: a RegionButton with a baked
  // circle Fill and a 56×56 extent. The GdUnit root is the test subproject, so authored
  // scenes under scenes/ cannot be loaded — pack a proto in code.
  private static PackedScene PackMarkerProto()
  {
    var proto = new RegionButton { Size = new Vector2(56, 56) };
    var fill = new Polygon2D
    {
      Name = "Fill",
      Polygon = CirclePolygon(28f, new Vector2(28, 28)),
    };
    proto.AddChild(fill);
    fill.Owner = proto; // Pack serializes only nodes owned by the scene root
    var scene = new PackedScene();
    scene.Pack(proto);
    proto.Free();
    return scene;
  }

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

  private static readonly Vector2[] Square =
  [
    new Vector2(10, 10), new Vector2(110, 10), new Vector2(110, 110), new Vector2(10, 110),
  ];

  // Authored map bounds passed to Setup — no assertion depends on the value (all test
  // events are region-targeted); it exercises the real parameter shape.
  private static readonly Vector2I TestMapSize = new(1600, 900);

  [TestCase(TestName = "A region without an authored button throws")]
  public void RegionWithoutButtonThrows()
  {
    var map = BuildMap([RegionButtonNamed("Alpha", Square)]);
    using var campaign = new GeoscapeFixture(
      regions: [TestData.MakeRegion("Alpha"), TestData.MakeRegion("Beta")]);

    Assert.Throws<InvalidOperationException>(() => map.Setup(campaign.Session, TestMapSize));
  }

  [TestCase(TestName = "An authored button without region data throws")]
  public void ButtonWithoutRegionThrows()
  {
    var map = BuildMap([RegionButtonNamed("Alpha", Square), RegionButtonNamed("Extra", Square)]);
    using var campaign = new GeoscapeFixture(regions: [TestData.MakeRegion("Alpha")]);

    Assert.Throws<InvalidOperationException>(() => map.Setup(campaign.Session, TestMapSize));
  }

  [TestCase(TestName = "Non-button children of the map are ignored")]
  public void NonButtonChildrenAreIgnored()
  {
    var map = BuildMap([RegionButtonNamed("Alpha", Square)]);
    map.AddChild(new Label { Name = "SomeLabel" });
    using var campaign = new GeoscapeFixture(regions: [TestData.MakeRegion("Alpha")]);

    map.Setup(campaign.Session, TestMapSize); // must not throw
  }

  [TestCase(TestName = "Setup without an assigned marker scene throws")]
  public void MissingMarkerSceneThrows()
  {
    var map = AutoFree(new GeoscapeMapControl()); // no EventMarkerScene assigned
    map.AddChild(RegionButtonNamed("Alpha", Square));
    using var campaign = new GeoscapeFixture(regions: [TestData.MakeRegion("Alpha")]);

    Assert.Throws<InvalidOperationException>(() => map.Setup(campaign.Session, TestMapSize));
  }

  [TestCase(TestName = "RefreshEvents stamps a marker button centered on the target region's centroid")]
  public void RefreshEventsLayersMarkerAtCentroid()
  {
    var map = BuildMap([RegionButtonNamed("Alpha", Square)]);
    using var campaign = new GeoscapeFixture(
      regions: [TestData.MakeRegion("Alpha")],
      timeline:
      [
        TestData.MakeScheduled(1, TestData.MakeEvent(
          "Raid", GeoscapeEventKind.TacticalBattle, targetRegionName: "Alpha")),
      ]);
    campaign.AdvanceTicks(1); // fires the raid at tick 1

    map.Setup(campaign.Session, TestMapSize);
    map.RefreshEvents();

    var marker = map.GetChild(map.GetChildCount() - 1);
    Assert.True(marker is RegionButton);
    var markerButton = (RegionButton)marker;
    // Later sibling → drawn above the regions, so overlapping hits land on the marker.
    // Centroid (60,60) − half the marker scene's extent (28,28).
    Assert.Equal(new Vector2(32, 32), markerButton.Position);
    Assert.True(markerButton._HasPoint(new Vector2(28, 28)));
    Assert.False(markerButton._HasPoint(new Vector2(2, 2)));
  }

  [TestCase(TestName = "Pressing a region button emits RegionClicked")]
  public void RegionPressedEmitsRegionClicked()
  {
    var map = BuildMap([RegionButtonNamed("Alpha", Square)]);
    using var campaign = new GeoscapeFixture(regions: [TestData.MakeRegion("Alpha")]);
    map.Setup(campaign.Session, TestMapSize);

    string? clicked = null;
    map.RegionClicked += region => clicked = region.Name;
    ((RegionButton)map.GetChild(0)).EmitSignal(BaseButton.SignalName.Pressed);

    Assert.Equal("Alpha", clicked);
  }

  [TestCase(TestName = "An authored Anchor child places the marker, not the centroid")]
  public void AuthoredAnchorPlacesMarker()
  {
    var map = BuildMap([RegionButtonNamed("Alpha", Square)]);
    ((RegionButton)map.GetChild(0)).AddChild(new Marker2D { Name = "Anchor", Position = new Vector2(500, 400) });
    using var campaign = new GeoscapeFixture(
      regions: [TestData.MakeRegion("Alpha")],
      timeline: [TestData.MakeScheduled(1, TestData.MakeEvent("Raid", targetRegionName: "Alpha"))]);
    campaign.AdvanceTicks(1);

    map.Setup(campaign.Session, TestMapSize);
    map.RefreshEvents();

    var marker = (RegionButton)map.GetChild(map.GetChildCount() - 1);
    Assert.Equal(new Vector2(472, 372), marker.Position); // anchor (500,400) − half extent (28,28)
  }

  [TestCase(TestName = "A resolved event's marker disappears from the map")]
  public void MarkerDisappearsAfterResolutionCompletes()
  {
    var map = BuildMap([RegionButtonNamed("Alpha", Square)]);
    using var campaign = new GeoscapeFixture(
      regions: [TestData.MakeRegion("Alpha")],
      timeline: [TestData.MakeScheduled(1, TestData.MakeEvent("Raid", targetRegionName: "Alpha"))]);
    campaign.AdvanceTicks(1);
    map.Setup(campaign.Session, TestMapSize);
    map.RefreshEvents();
    Assert.Equal(2, map.GetChildCount()); // region button + marker button

    campaign.OpenResolution(campaign.ActiveEvent);
    campaign.CompleteResolution(ResolutionOutcome.Acknowledged);
    map.RefreshEvents(); // what the composition root does on ResolutionEventClosed

    Assert.Equal(1, map.GetChildCount()); // the marker is gone
  }

  [TestCase(TestName = "Pressing a marker button emits EventClicked with the active event")]
  public void MarkerPressedEmitsEventClicked()
  {
    var map = BuildMap([RegionButtonNamed("Alpha", Square)]);
    using var campaign = new GeoscapeFixture(
      regions: [TestData.MakeRegion("Alpha")],
      timeline: [TestData.MakeScheduled(1, TestData.MakeEvent("Raid", targetRegionName: "Alpha"))]);
    campaign.AdvanceTicks(1);

    map.Setup(campaign.Session, TestMapSize);
    map.RefreshEvents();

    GeoscapeEventAdapter? envelope = null;
    map.EventClicked += adapter => envelope = adapter;
    ((RegionButton)map.GetChild(map.GetChildCount() - 1)).EmitSignal(BaseButton.SignalName.Pressed);

    Assert.That(envelope != null);
    Assert.Equal("Raid", envelope!.Event.Definition.Title);
  }
}
