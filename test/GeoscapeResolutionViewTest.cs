using System.Threading.Tasks;
using FunProject.Geoscape;
using FunProject.Strategic;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;
using static FunProject.Tests.GeoscapeTestScenes;

[TestSuite]
[RequireGodotRuntime]
public class GeoscapeResolutionViewTest
{
  // Direct-view contracts: rendering the session's pending resolution (region suffix
  // included) and the button -> outcome mapping. Presenting without a pending resolution
  // is a caller bug. The dialog is a transparent GeoscapeView with no backdrop of its own.
  [TestCase]
  public async Task PresentRendersPendingResolutionWithRegionAndMapsTacticalOutcomes()
  {
    await using var cleanup = new DeferredNodeCleanup();
    var view = AddToTree(CreateResolutionView());
    using var campaign = GeoscapeFixture.WithFiredEvent(
      MakeEvent("Distress call", GeoscapeEventKind.TacticalBattle, "Northmark"),
      [MakeRegion("Northmark")]);
    campaign.OpenResolution(campaign.ActiveEvent);

    view.Present(campaign.State, campaign.Session);
    view.Present(campaign.State, campaign.Session); // re-presentation rebuilds, never stacks

    Assert.Equal("Distress call", view.GetNode<Label>("%Title").Text);
    Assert.Equal("[TacticalBattle — Northmark]\nDistress call description.",
      view.GetNode<Label>("%Description").Text);
    var buttons = view.GetNode<HBoxContainer>("%Buttons");
    Assert.Equal(2, buttons.GetChildCount());
    Assert.Equal("Engage", buttons.GetChild<Button>(0).Text);
    Assert.Equal("Decline", buttons.GetChild<Button>(1).Text);

    SysColGeneric.List<ResolutionOutcome> outcomes = [];
    view.Resolved += outcomes.Add;
    buttons.GetChild<Button>(1).EmitSignal(Button.SignalName.Pressed); // Decline resolves
    Assert.Equal(1, outcomes.Count);
    Assert.Equal(ResolutionOutcome.Declined, outcomes[0]);

    // Engage navigates instead: it produces the squad view from the authored export.
    GeoscapeView? produced = null;
    view.ViewRequested += requested => produced = requested;
    buttons.GetChild<Button>(0).EmitSignal(Button.SignalName.Pressed);
    Assert.True(produced is SquadLoadoutView);
    Assert.True(produced!.GetParent() is null); // emitted live and unparented
    Assert.True(campaign.Session.PendingResolution.IsSome); // the mission stays pending
    produced.Free(); // never pushed here; free directly
  }

  [TestCase]
  public void PresentRendersMapWideEventWithSingleContinueButton()
  {
    var view = AddToTree(CreateResolutionView());
    using var campaign = GeoscapeFixture.WithFiredEvent(MakeEvent("Plot beat"));
    campaign.OpenResolution(campaign.ActiveEvent);

    view.Present(campaign.State, campaign.Session);

    Assert.Equal("[Plot]\nPlot beat description.", view.GetNode<Label>("%Description").Text);
    var buttons = view.GetNode<HBoxContainer>("%Buttons");
    Assert.Equal(1, buttons.GetChildCount());
    Assert.Equal("Continue", buttons.GetChild<Button>(0).Text);
  }

  [TestCase]
  public void PresentWithoutPendingResolutionThrows()
  {
    var view = AddToTree(CreateResolutionView());
    using var campaign = new GeoscapeFixture();

    Assert.Throws<System.InvalidOperationException>(
      () => view.Present(campaign.State, campaign.Session));
  }

  // Scene-level wiring: the composition root pushes the dialog on the session's opened
  // event and pops it on the committed close, restoring whatever the dialog covered.
  [TestCase]
  public async Task MapEventPressPushesDialogAndDeclinePopsBackToCoveredRoot()
  {
    await using var cleanup = new DeferredNodeCleanup();
    // Regions need authored map visuals (GeoscapeMapControl.Setup), so the scene-level
    // test drives a map-wide event; region-suffix rendering is covered by the direct
    // view tests above.
    var scene = AddToTree(CreateGeoscapeScene(MakeStart(
      timeline:
      [
        MakeScheduled(1, MakeEvent("Distress call", GeoscapeEventKind.TacticalBattle)),
        MakeScheduled(1, MakeEvent("Weather report")),
      ])));
    var manager = scene.GetNode<GeoscapeViewManager>("%ViewManager");
    var hud = scene.GetNode<GeoscapeHud>("%GeoscapeHud");
    hud.GetNode<Button>("%SpeedButton").EmitSignal(Button.SignalName.Pressed); // 5x, running
    scene._PhysicsProcess(0.02); // one tick at 5x fires the scheduled event

    OpenResolutionViaMapEvent(scene);

    var dialog = (GeoscapeEventResolution)manager.Current;
    OpenResolutionViaMapEvent(scene); // repeated clicks cannot stack the same resolution
    OpenResolutionViaMapEvent(scene, eventIndex: 1); // another event cannot replace the pending one
    Assert.True(ReferenceEquals(dialog, manager.Current));
    Assert.Equal(1, manager.GetChildren().AsValueEnumerable().OfType<GeoscapeEventResolution>().Count());
    Assert.Equal(2, MapEventMarkers(scene).Length); // both events remain active while pending
    Assert.True(hud.IsVisibleInTree()); // the root view is covered by the dialog
    Assert.Equal("Distress call", dialog.GetNode<Label>("%Title").Text);
    Assert.Equal("[TacticalBattle]\nDistress call description.",
      dialog.GetNode<Label>("%Description").Text);

    dialog.GetNode<HBoxContainer>("%Buttons").GetChild<Button>(1)
      .EmitSignal(Button.SignalName.Pressed); // Decline resolves through the session

    Assert.True(ReferenceEquals(manager.RootView, manager.Current));
    Assert.True(dialog.GetParent() is null); // popped views detach immediately
    Assert.True(hud.IsVisibleInTree());
    Assert.Equal(1, MapEventMarkers(scene).Length); // only the resolved event left the map
    OpenResolutionViaMapEvent(scene);
    var nextDialog = (GeoscapeEventResolution)manager.Current;
    Assert.Equal("Weather report", nextDialog.GetNode<Label>("%Title").Text);
    nextDialog.GetNode<HBoxContainer>("%Buttons").GetChild<Button>(0)
      .EmitSignal(Button.SignalName.Pressed);
    Assert.True(ReferenceEquals(manager.RootView, manager.Current));
    Assert.Equal(0, MapEventMarkers(scene).Length);
    await WaitForDeferredDeletion((SceneTree)Engine.GetMainLoop());
    Assert.False(GodotObject.IsInstanceValid(dialog));
    Assert.False(GodotObject.IsInstanceValid(nextDialog));
  }

  [TestCase]
  public async Task MapEventMarkerDisappearsAtItsDeadlineWithoutRemovingOtherEvents()
  {
    await using var cleanup = new DeferredNodeCleanup();
    var scene = AddToTree(CreateGeoscapeScene(MakeStart(timeline:
    [
      MakeScheduled(1, MakeEvent("Brief", expiresAfterTicks: 2)),
      MakeScheduled(1, MakeEvent("Persistent")),
    ])));
    var manager = scene.GetNode<GeoscapeViewManager>("%ViewManager");
    SpeedButton(scene).EmitSignal(Button.SignalName.Pressed); // 5x
    scene._PhysicsProcess(0.02); // tick 1: both events fire
    Assert.Equal(2, MapEventMarkers(scene).Length);
    scene._PhysicsProcess(0.02); // tick 2: Brief is still active
    Assert.Equal(2, MapEventMarkers(scene).Length);

    scene._PhysicsProcess(0.02); // tick 3: Brief expires at its captured deadline

    Assert.Equal(1, MapEventMarkers(scene).Length);
    OpenResolutionViaMapEvent(scene);
    Assert.Equal("Persistent", manager.Current.GetNode<Label>("%Title").Text);
  }
}
