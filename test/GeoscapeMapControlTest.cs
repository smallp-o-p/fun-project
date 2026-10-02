#nullable disable warnings
using FunProject.Strategic;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using static FunProject.Tests.GeoscapeTestScenes;

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
    var map = AutoFree(CreateMap(buttons));
    ((SceneTree)Engine.GetMainLoop()).Root.AddChild(map);
    return map;
  }

  private static GeoscapeMapControl CreateMap(RegionButton[] buttons)
  {
    var map = new GeoscapeMapControl
    {
      EventMarkerScene = GD.Load<PackedScene>("res://scenes/geoscape/GeoscapeEventMarker.tscn"),
    };
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

  // Authored map bounds passed to Setup; marker geometry stays in this coordinate space.
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
    Assert.True(ReferenceEquals(campaign.ActiveEvent, envelope.Event));

    campaign.OpenResolution(campaign.ActiveEvent);
    campaign.CompleteResolution(ResolutionOutcome.Acknowledged);
    map.RefreshEvents(); // what the composition root does on ResolutionEventClosed

    Assert.Equal(1, map.GetChildCount()); // the marker is gone
  }

  [TestCase(false, TestName = "Map-wide and nearby top-left event clusters are individually selectable")]
  [TestCase(true, TestName = "Map-wide and nearby bottom-right event clusters are individually selectable")]
  public async Task ClusteredEventsStayInsideMapAndRouteActualClicks(bool bottomRight)
  {
    await using var cleanup = new DeferredNodeCleanup();
    var mapSize = new Vector2I(560, 336);
    Vector2 edge = bottomRight ? (Vector2)mapSize - new Vector2(4, 4) : new Vector2(4, 4);
    Vector2 nearby = edge + (bottomRight ? -1 : 1) * new Vector2(12, 12);
    var alpha = RegionButtonNamed("Alpha", Square);
    var beta = RegionButtonNamed("Beta", Square);
    alpha.AddChild(new Marker2D { Name = "Anchor", Position = edge });
    beta.AddChild(new Marker2D { Name = "Anchor", Position = nearby });
    var map = CreateMap([alpha, beta]);
    var viewport = CreateUiViewport(map, mapSize);
    var timeline = new List<ScheduledEventData>();
    string[] targets = ["", "", "", "Alpha", "Alpha", "Alpha", "Alpha", "Beta", "Beta", "Beta"];
    for (int index = 0; index < targets.Length; index++)
      timeline.Add(TestData.MakeScheduled(1, TestData.MakeEvent(
        $"Event {index}", targetRegionName: targets[index], expiresAfterTicks: 3)));
    using var campaign = new GeoscapeFixture(
      regions: [TestData.MakeRegion("Alpha"), TestData.MakeRegion("Beta")], timeline: [.. timeline]);
    campaign.AdvanceTicks(1);
    map.Setup(campaign.Session, mapSize);
    map.RefreshEvents();
    await WaitForLayout(viewport);

    // These clicks go through Godot hit-testing, so a hidden marker cannot pass merely
    // because its Pressed signal remains wired to the right event.
    AssertMarkersSelectable(map, viewport, campaign.Session.ActiveEvents);
    RegionButton[] markers = Markers(map);
    Assert.Equal((Vector2)mapSize / 2f, markers[0].GetRect().GetCenter());
    AssertMarkerGeometry(markers, mapSize);
    Vector2[] positions = markers.AsValueEnumerable().Select(marker => marker.Position).ToArray();

    map.RefreshEvents();
    Assert.True(markers.AsValueEnumerable().All(marker => marker.GetParent() is null));
    await WaitForLayout(viewport);
    markers = Markers(map);
    for (int index = 0; index < markers.Length; index++)
      Assert.Equal(positions[index], markers[index].Position, "Refreshing unchanged events must not move them.");
    AssertMarkersSelectable(map, viewport, campaign.Session.ActiveEvents);

    // Resolution removes only its own event; the remaining cluster still exposes every
    // original event identity. Expiry then removes every remaining marker.
    campaign.OpenResolution(campaign.Session.ActiveEvents[3]);
    campaign.CompleteResolution(ResolutionOutcome.Acknowledged);
    map.RefreshEvents();
    Assert.True(markers.AsValueEnumerable().All(marker => marker.GetParent() is null));
    await WaitForLayout(viewport);
    Assert.Equal(targets.Length - 1, Markers(map).Length);
    AssertMarkerGeometry(Markers(map), mapSize);
    AssertMarkersSelectable(map, viewport, campaign.Session.ActiveEvents);

    markers = Markers(map);
    campaign.AdvanceTicks(3);
    map.RefreshEvents();
    Assert.True(markers.AsValueEnumerable().All(marker => marker.GetParent() is null));
    Assert.Equal(0, Markers(map).Length);
    Assert.Equal(2, map.GetChildCount()); // only the authored regions remain
  }

  [TestCase(TestName = "A crowded small map scales every marker equally without hiding event choices")]
  public async Task CrowdedMapRetainsEverySelectableEvent()
  {
    await using var cleanup = new DeferredNodeCleanup();
    var mapSize = new Vector2I(112, 112);
    var map = CreateMap([RegionButtonNamed("Alpha", Square)]);
    var viewport = CreateUiViewport(map, mapSize);
    var timeline = new List<ScheduledEventData>();
    for (int index = 0; index < 8; index++)
      timeline.Add(TestData.MakeScheduled(1, TestData.MakeEvent($"Event {index}")));
    using var campaign = new GeoscapeFixture(regions: [TestData.MakeRegion("Alpha")], timeline: [.. timeline]);
    campaign.AdvanceTicks(1);
    map.Setup(campaign.Session, mapSize);
    map.RefreshEvents();
    await WaitForLayout(viewport);

    AssertMarkersSelectable(map, viewport, campaign.Session.ActiveEvents);
    RegionButton[] markers = Markers(map);
    AssertMarkerGeometry(markers, mapSize);
    Assert.True(markers[0].Scale.X > 0 && markers[0].Scale.X < 1,
      "Eight full-size 56px markers cannot fit in a 112px square map.");
    foreach (RegionButton marker in markers)
    {
      Assert.Equal(markers[0].Scale, marker.Scale);
      Assert.Equal(marker.Scale.X, marker.Scale.Y);
      Assert.False(marker.Disabled);
      Assert.True(marker.Visible);
    }
  }

  private static RegionButton[] Markers(GeoscapeMapControl map)
    => map.GetChildren().AsValueEnumerable().OfType<RegionButton>()
      .Where(button => button.SceneFilePath == map.EventMarkerScene.ResourcePath).ToArray();

  private static void AssertMarkerGeometry(RegionButton[] markers, Vector2I mapSize)
  {
    for (int index = 0; index < markers.Length; index++)
    {
      Rect2 rect = ScreenRect(markers[index]);
      Assert.True(rect.Position.X >= 0 && rect.Position.Y >= 0);
      Assert.True(rect.End.X <= mapSize.X && rect.End.Y <= mapSize.Y);
      for (int other = 0; other < index; other++)
        Assert.False(rect.Intersects(ScreenRect(markers[other])), "Full marker rectangles must not overlap.");
    }
  }

  private static void AssertMarkersSelectable(
    GeoscapeMapControl map, SubViewport viewport, IReadOnlyList<GeoscapeEvent> events)
  {
    RegionButton[] markers = Markers(map);
    Assert.Equal(events.Count, markers.Length);
    var clicked = new List<GeoscapeEvent>();
    void Record(GeoscapeEventAdapter adapter) => clicked.Add(adapter.Event);
    map.EventClicked += Record;
    try
    {
      viewport.NotifyMouseEntered();
      for (int index = 0; index < markers.Length; index++)
      {
        Vector2 center = ScreenRect(markers[index]).GetCenter();
        viewport.PushInput(new InputEventMouseMotion { Position = center, GlobalPosition = center }, true);
        foreach (bool pressed in new[] { true, false })
          viewport.PushInput(new InputEventMouseButton
          {
            ButtonIndex = MouseButton.Left,
            Pressed = pressed,
            Position = center,
            GlobalPosition = center,
          }, true);
        Assert.Equal(index + 1, clicked.Count, "Each marker center must deliver exactly one event click.");
        Assert.True(ReferenceEquals(events[index], clicked[index]), "The clicked marker must preserve event identity.");
      }
    }
    finally
    {
      map.EventClicked -= Record;
    }
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
