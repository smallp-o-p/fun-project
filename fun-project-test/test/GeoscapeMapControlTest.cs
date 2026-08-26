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
  // optional offset). Only the map needs AutoFree — children free transitively.
  private static GeoscapeMapControl BuildMap(RegionButton[] buttons)
  {
    var map = AutoFree(new GeoscapeMapControl());
    foreach (RegionButton button in buttons)
      map.AddChild(button);
    return map;
  }

  private static RegionButton RegionButtonNamed(string name, Vector2[] polygon, Vector2 fillOffset = default)
  {
    var button = new RegionButton { Name = name };
    button.AddChild(new Polygon2D { Name = "Fill", Polygon = polygon, Position = fillOffset });
    return button;
  }

  private static readonly Vector2[] Square =
  [
    new Vector2(10, 10), new Vector2(110, 10), new Vector2(110, 110), new Vector2(10, 110),
  ];

  [TestCase(TestName = "Setup derives the region hit area from the authored Fill polygon")]
  public void SetupDerivesHitAreaFromFill()
  {
    var map = BuildMap(
    [
      RegionButtonNamed("Alpha",
      [
        new Vector2(0, 0), new Vector2(100, 0), new Vector2(100, 100), new Vector2(0, 100),
      ], fillOffset: new Vector2(10, 10)),
    ]);
    var session = GeoscapeTestFactory.MakeSession(regions: [GeoscapeTestFactory.MakeRegion("Alpha")]);

    map.Setup(session);
    var button = (RegionButton)map.GetChild(0);

    Assert.True(button._HasPoint(new Vector2(60, 60))); // effective area (10..110)
    Assert.True(button._HasPoint(new Vector2(11, 11)));
    Assert.False(button._HasPoint(new Vector2(5, 5))); // below the Fill offset
    Assert.False(button._HasPoint(new Vector2(150, 150)));
  }

  [TestCase(TestName = "A region without an authored button throws")]
  public void RegionWithoutButtonThrows()
  {
    var map = BuildMap([RegionButtonNamed("Alpha", Square)]);
    var session = GeoscapeTestFactory.MakeSession(
      regions: [GeoscapeTestFactory.MakeRegion("Alpha"), GeoscapeTestFactory.MakeRegion("Beta")]);

    Assert.Throws<InvalidOperationException>(() => map.Setup(session));
  }

  [TestCase(TestName = "An authored button without region data throws")]
  public void ButtonWithoutRegionThrows()
  {
    var map = BuildMap([RegionButtonNamed("Alpha", Square), RegionButtonNamed("Extra", Square)]);
    var session = GeoscapeTestFactory.MakeSession(regions: [GeoscapeTestFactory.MakeRegion("Alpha")]);

    Assert.Throws<InvalidOperationException>(() => map.Setup(session));
  }

  [TestCase(TestName = "Non-button children of the map are ignored")]
  public void NonButtonChildrenAreIgnored()
  {
    var map = BuildMap([RegionButtonNamed("Alpha", Square)]);
    map.AddChild(new Label { Name = "SomeLabel" });
    var session = GeoscapeTestFactory.MakeSession(regions: [GeoscapeTestFactory.MakeRegion("Alpha")]);

    map.Setup(session); // must not throw
  }

  [TestCase(TestName = "RefreshEvents layers a marker button on the target region's centroid")]
  public void RefreshEventsLayersMarkerAtCentroid()
  {
    var map = BuildMap([RegionButtonNamed("Alpha", Square)]);
    var session = GeoscapeTestFactory.MakeSession(
      regions: [GeoscapeTestFactory.MakeRegion("Alpha")],
      timeline:
      [
        GeoscapeTestFactory.MakeScheduled(1, GeoscapeTestFactory.MakeEvent(
          "Raid", GeoscapeEventKind.TacticalBattle, targetRegionName: "Alpha")),
      ]);
    session.ChangeSpeed(TimeSpeed.Normal);
    session.Advance(0.1); // fires the raid at tick 1

    map.Setup(session);
    map.RefreshEvents();

    var marker = map.GetChild(map.GetChildCount() - 1);
    Assert.True(marker is RegionButton);
    var markerButton = (RegionButton)marker;
    // Later sibling → drawn above the regions, so overlapping hits land on the marker.
    Assert.Equal(new Vector2(32, 32), markerButton.Position); // centroid (60,60) − corner (28,28)
    Assert.True(markerButton._HasPoint(new Vector2(28, 28)));
    Assert.False(markerButton._HasPoint(new Vector2(2, 2)));
  }

  [TestCase(TestName = "Pressing a region button emits RegionClicked")]
  public void RegionPressedEmitsRegionClicked()
  {
    var map = BuildMap([RegionButtonNamed("Alpha", Square)]);
    var session = GeoscapeTestFactory.MakeSession(regions: [GeoscapeTestFactory.MakeRegion("Alpha")]);
    map.Setup(session);

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
    var session = GeoscapeTestFactory.MakeSession(
      regions: [GeoscapeTestFactory.MakeRegion("Alpha")],
      timeline: [GeoscapeTestFactory.MakeScheduled(1, GeoscapeTestFactory.MakeEvent("Raid", targetRegionName: "Alpha"))]);
    session.ChangeSpeed(TimeSpeed.Normal);
    session.Advance(0.1);

    map.Setup(session);
    map.RefreshEvents();

    var marker = (RegionButton)map.GetChild(map.GetChildCount() - 1);
    Assert.Equal(new Vector2(472, 372), marker.Position); // anchor (500,400) − corner (28,28)
  }

  [TestCase(TestName = "A resolved event's marker disappears from the map")]
  public void MarkerDisappearsAfterResolutionCompletes()
  {
    var map = BuildMap([RegionButtonNamed("Alpha", Square)]);
    var session = GeoscapeTestFactory.MakeSession(
      regions: [GeoscapeTestFactory.MakeRegion("Alpha")],
      timeline: [GeoscapeTestFactory.MakeScheduled(1, GeoscapeTestFactory.MakeEvent("Raid", targetRegionName: "Alpha"))]);
    session.ChangeSpeed(TimeSpeed.Normal);
    session.Advance(0.1);
    map.Setup(session);
    map.RefreshEvents();
    Assert.Equal(2, map.GetChildCount()); // region button + marker button

    session.OpenResolution(session.ActiveEvents[0]);
    session.CompleteResolution(ResolutionOutcome.Acknowledged);
    map.RefreshEvents(); // what the composition root does on ResolutionEventClosed

    Assert.Equal(1, map.GetChildCount()); // the marker is gone
  }

  [TestCase(TestName = "Pressing a marker button emits EventClicked with the active event")]
  public void MarkerPressedEmitsEventClicked()
  {
    var map = BuildMap([RegionButtonNamed("Alpha", Square)]);
    var session = GeoscapeTestFactory.MakeSession(
      regions: [GeoscapeTestFactory.MakeRegion("Alpha")],
      timeline: [GeoscapeTestFactory.MakeScheduled(1, GeoscapeTestFactory.MakeEvent("Raid", targetRegionName: "Alpha"))]);
    session.ChangeSpeed(TimeSpeed.Normal);
    session.Advance(0.1);

    map.Setup(session);
    map.RefreshEvents();

    GeoscapeEventAdapter? envelope = null;
    map.EventClicked += adapter => envelope = adapter;
    ((RegionButton)map.GetChild(map.GetChildCount() - 1)).EmitSignal(BaseButton.SignalName.Pressed);

    Assert.That(envelope != null);
    Assert.Equal("Raid", envelope!.Event.Definition.Title);
  }
}
