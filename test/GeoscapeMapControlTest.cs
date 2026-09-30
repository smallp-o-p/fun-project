#nullable disable warnings
using FunProject.Strategic;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;
using System;
using System.Threading.Tasks;

[TestSuite]
[RequireGodotRuntime]
public class GeoscapeMapControlTest
{
  // Region visuals are the controlled geometry inputs: an authored-shape RegionButton
  // child of the map, named after its RegionData, with a Polygon2D "Fill" child. The map
  // enters the tree — tree entry stands in for scene load, so the buttons self-configure
  // (_Ready derives hit areas and wires hover). Only the map needs AutoFree — children
  // free transitively. Event markers come from the real authored marker scene.
  private static GeoscapeMapControl BuildMap(RegionButton[] buttons)
  {
    var map = AutoFree(new GeoscapeMapControl
    {
      EventMarkerScene = GD.Load<PackedScene>("res://scenes/geoscape/GeoscapeEventMarker.tscn"),
    });
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

  private static readonly Vector2[] Square =
  [
    new Vector2(10, 10), new Vector2(110, 10), new Vector2(110, 110), new Vector2(10, 110),
  ];

  // Authored map bounds passed to Setup — no assertion depends on the value (all test
  // events are region-targeted); it exercises the real parameter shape.
  private static readonly Vector2I TestMapSize = new(1600, 900);

  [TestCase(false, TestName = "A region without an authored button throws")]
  [TestCase(true, TestName = "An authored button without region data throws")]
  public void RegionAndButtonDataMustMatch(bool extraButton)
  {
    var map = BuildMap(extraButton
      ? [RegionButtonNamed("Alpha", Square), RegionButtonNamed("Extra", Square)]
      : [RegionButtonNamed("Alpha", Square)]);
    using var campaign = new GeoscapeFixture(regions: extraButton
      ? [TestData.MakeRegion("Alpha")]
      : [TestData.MakeRegion("Alpha"), TestData.MakeRegion("Beta")]);

    Assert.Throws<InvalidOperationException>(() => map.Setup(campaign.Session, TestMapSize));
  }

  [TestCase(TestName = "Setup without an assigned marker scene throws")]
  public void MissingMarkerSceneThrows()
  {
    var map = AutoFree(new GeoscapeMapControl()); // no EventMarkerScene assigned
    map.AddChild(RegionButtonNamed("Alpha", Square));
    using var campaign = new GeoscapeFixture(regions: [TestData.MakeRegion("Alpha")]);

    Assert.Throws<InvalidOperationException>(() => map.Setup(campaign.Session, TestMapSize));
  }

  [TestCase(false, GeoscapeEventKind.TacticalBattle,
    TestName = "RefreshEvents stamps a tactical marker button centered on the target region's centroid")]
  [TestCase(true, GeoscapeEventKind.Plot,
    TestName = "An authored Anchor child places the plot marker, not the centroid")]
  [TestCase(false, GeoscapeEventKind.Plot,
    TestName = "A plot marker routes clicks and disappears after the resolution completes")]
  public async Task MarkerUsesRegionAnchorAndRoutesItsLifecycle(bool explicitAnchor, GeoscapeEventKind kind)
  {
    await using var cleanup = new DeferredNodeCleanup();
    var map = BuildMap([RegionButtonNamed("Alpha", Square)]);
    if (explicitAnchor)
      ((RegionButton)map.GetChild(0)).AddChild(new Marker2D { Name = "Anchor", Position = new Vector2(500, 400) });
    using var campaign = new GeoscapeFixture(
      regions: [TestData.MakeRegion("Alpha")],
      timeline:
      [
        TestData.MakeScheduled(1, TestData.MakeEvent(
          "Raid", kind, targetRegionName: "Alpha")),
      ]);
    campaign.AdvanceTicks(1); // fires the event at tick 1

    map.Setup(campaign.Session, TestMapSize);
    map.RefreshEvents();

    var marker = map.GetChild(map.GetChildCount() - 1);
    Assert.Equal(2, map.GetChildCount()); // region button + marker button
    Assert.True(marker is RegionButton);
    var markerButton = (RegionButton)marker;
    // Later sibling → drawn above the regions, so overlapping hits land on the marker.
    // Centroid (60,60) − half the marker scene's extent (28,28), or the anchor (500,400) − extent.
    Assert.Equal(explicitAnchor ? new Vector2(472, 372) : new Vector2(32, 32), markerButton.Position);
    if (!explicitAnchor)
    {
      // The authored circle keeps its hit shape inside, not the button rect.
      Assert.True(markerButton._HasPoint(new Vector2(28, 28)));
      Assert.False(markerButton._HasPoint(new Vector2(2, 2)));
    }

    GeoscapeEventAdapter? envelope = null;
    map.EventClicked += adapter => envelope = adapter;
    markerButton.EmitSignal(BaseButton.SignalName.Pressed);
    Assert.That(envelope != null);
    Assert.Equal("Raid", envelope!.Event.Definition.Title);

    campaign.OpenResolution(campaign.ActiveEvent);
    campaign.CompleteResolution(ResolutionOutcome.Acknowledged);
    map.RefreshEvents(); // what the composition root does on ResolutionEventClosed

    Assert.Equal(1, map.GetChildCount()); // the marker is gone
  }

  [TestCase(TestName = "Pressing a region button emits RegionClicked")]
  public void RegionPressedEmitsRegionClicked()
  {
    // Non-button children of the map are ignored: Setup must succeed with one present.
    var map = BuildMap([RegionButtonNamed("Alpha", Square)]);
    map.AddChild(new Label { Name = "SomeLabel" });
    using var campaign = new GeoscapeFixture(regions: [TestData.MakeRegion("Alpha")]);
    map.Setup(campaign.Session, TestMapSize);

    string? clicked = null;
    map.RegionClicked += region => clicked = region.Name;
    ((RegionButton)map.GetChild(0)).EmitSignal(BaseButton.SignalName.Pressed);

    Assert.Equal("Alpha", clicked);
  }
}
