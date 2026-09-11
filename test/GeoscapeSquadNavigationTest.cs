#nullable disable warnings
using System;
using System.Threading.Tasks;
using FunProject.Geoscape;
using FunProject.GameState;
using FunProject.Items;
using FunProject.Strategic;
using Godot;
using GdUnit4;
using static FunProject.Tests.GeoscapeTestScenes;

[TestSuite]
[RequireGodotRuntime]
public class GeoscapeSquadNavigationTest
{
  // Authored geoscape with a tactical mission firing at tick 1. One SpeedButton press
  // un-pauses to Normal; a single physics step fires the mission. Opening the mission
  // pushes the resolution dialog as a stacked view; Engage produces the squad view from
  // the dialog and requests it as the next stacked view.
  private static (GeoscapeScene Scene, GeoscapeViewManager Manager) TacticalScene(
    RosterEntryData[] roster = null, EquippableItemData[] armory = null)
  {
    var scene = AddToTree(CreateGeoscapeScene(MakeStart(
      roster: roster,
      armory: armory,
      timeline: [MakeScheduled(1, MakeEvent("Operation Iron", GeoscapeEventKind.TacticalBattle))])));
    var manager = scene.GetNode<GeoscapeViewManager>("%ViewManager");
    SpeedButton(scene).EmitSignal(Button.SignalName.Pressed);
    scene._PhysicsProcess(0.1);
    return (scene, manager);
  }

  private static Button SpeedButton(GeoscapeScene scene)
    => scene.GetNode<GeoscapeHud>("%GeoscapeHud").GetNode<Button>("%SpeedButton");

  private static Label Clock(GeoscapeScene scene)
    => scene.GetNode<GeoscapeHud>("%GeoscapeHud").GetNode<Label>("%ClockLabel");

  private static Button DialogButton(GeoscapeEventResolution dialog, string text)
    => dialog.GetNode<HBoxContainer>("%Buttons").GetChildren()
      .AsValueEnumerable().OfType<Button>()
      .Single(button => button.Text == text);

  private static void OpenMissionViaAlert(GeoscapeScene scene)
    => scene.GetNode<GeoscapeHud>("%GeoscapeHud").GetNode<VBoxContainer>("%Alerts")
      .GetChild<Button>(0).EmitSignal(Button.SignalName.Pressed);

  private static void OpenMissionViaMarker(GeoscapeScene scene)
  {
    var map = scene.GetNode<GeoscapeMapControl>("%Map");
    ((RegionButton)map.GetChild(map.GetChildCount() - 1))
      .EmitSignal(BaseButton.SignalName.Pressed);
  }

  private static void ChooseIntoSlot(SquadLoadoutView view, int slot, string unitName)
  {
    view.GetNode<VBoxContainer>("%Slots").GetChild<Control>(slot)
      .GetNode<Button>("VBox/Header/ChooseUnit").EmitSignal(Button.SignalName.Pressed);
    view.GetNode<VBoxContainer>("%RosterChoices").GetChildren()
      .AsValueEnumerable().OfType<Button>()
      .Single(button => button.Text.Contains(unitName)).EmitSignal(Button.SignalName.Pressed);
  }

  private static int AlertCount(GeoscapeScene scene)
    => scene.GetNode<GeoscapeHud>("%GeoscapeHud").GetNode<VBoxContainer>("%Alerts").GetChildCount();

  [TestCase(TestName = "Engage produces a squad view without resolving the mission")]
  public async Task EngageProducesTheSquadViewWithoutResolving()
  {
    await using var cleanup = new DeferredNodeCleanup();
    using var fixture = GeoscapeFixture.WithFiredEvent(
      MakeEvent("Operation", GeoscapeEventKind.TacticalBattle));
    fixture.OpenResolution(fixture.ActiveEvent);
    var dialog = AddToTree(CreateResolutionView());
    dialog.Present(fixture.State, fixture.Session);
    fixture.ClearEvents();

    GeoscapeView? produced = null;
    dialog.ViewRequested += view => produced = view;
    DialogButton(dialog, "Engage").EmitSignal(Button.SignalName.Pressed);

    Assert.True(produced is SquadLoadoutView); // the authored SquadViewScene destination
    Assert.True(produced!.GetParent() is null); // emitted live and unparented, not pushed
    Assert.True(fixture.Session.PendingResolution.IsSome);
    Assert.Equal(0, fixture.Events.Count); // no resolution lifecycle was committed
    produced.Free(); // the standalone dialog never pushed it; free directly
  }

  [TestCase(TestName = "Missing and wrong-root squad destinations are authoring errors")]
  public async Task BadSquadDestinationsThrowAndLeaveStateUnchanged()
  {
    await using var cleanup = new DeferredNodeCleanup();
    using var fixture = GeoscapeFixture.WithFiredEvent(
      MakeEvent("Operation", GeoscapeEventKind.TacticalBattle));
    fixture.OpenResolution(fixture.ActiveEvent);
    var dialog = AddToTree(CreateResolutionView());
    dialog.Present(fixture.State, fixture.Session);
    fixture.ClearEvents();

    // Signal handlers swallow exceptions, so the guards are invoked directly.
    dialog.SquadViewScene = null;
    Assert.Throws<InvalidOperationException>(() => dialog.RequestView(dialog.SquadViewScene));

    var probe = new Label { Name = "NotAView" };
    var packed = new PackedScene();
    Error error = packed.Pack(probe);
    probe.Free();
    if (error != Error.Ok)
      throw new InvalidOperationException($"Test scene packing failed: {error}");
    dialog.SquadViewScene = packed;
    Assert.Throws<InvalidOperationException>(() => dialog.RequestView(dialog.SquadViewScene));

    Assert.True(fixture.Session.PendingResolution.IsSome);
    Assert.Equal(0, fixture.Events.Count);
  }

  [TestCase(TestName = "Marker route: Engage covers the dialog with squad preparation")]
  public async Task MarkerEngageOpensSquadPreparation()
  {
    await using var cleanup = new DeferredNodeCleanup();
    var (scene, manager) = TacticalScene([MakeEntry("Alpha")]);
    OpenMissionViaMarker(scene);
    var dialog = (GeoscapeEventResolution)manager.Current;

    DialogButton(dialog, "Engage").EmitSignal(Button.SignalName.Pressed);

    var squad = (SquadLoadoutView)manager.Current;
    Assert.Equal("Operation Iron", squad.GetNode<Label>("%Title").Text);
    Assert.False(dialog.IsVisibleInTree()); // squad preparation covers the dialog
    Assert.True(squad.IsVisibleInTree());

    string clock = Clock(scene).Text;
    scene._PhysicsProcess(1.0); // covered views freeze the clock at the composition root
    Assert.Equal(clock, Clock(scene).Text);
    Assert.Equal(1, AlertCount(scene)); // the mission stays active: alert remains
  }

  [TestCase(TestName = "Full round trip keeps the mission and Decline still resolves")]
  public async Task FullRoundTripKeepsMissionAndDeclineResolves()
  {
    await using var cleanup = new DeferredNodeCleanup();
    var (scene, manager) = TacticalScene(
      [MakeEntry("Alpha"), MakeEntry("Bravo")],
      armory: [MakeFirearmWeaponData("Rifle")]);
    OpenMissionViaAlert(scene);
    var dialog = (GeoscapeEventResolution)manager.Current;
    DialogButton(dialog, "Engage").EmitSignal(Button.SignalName.Pressed);
    var squad = (SquadLoadoutView)manager.Current;

    ChooseIntoSlot(squad, 0, "Alpha");
    squad.GetNode<VBoxContainer>("%Slots").GetChild<Control>(0)
      .GetNode<Button>("%EditUnit").EmitSignal(Button.SignalName.Pressed);
    var editor = (UnitView)manager.Current;
    Assert.False(squad.IsVisibleInTree()); // the nested editor covers preparation
    editor.SelectSlot(UnitViewSlot.Weapon);
    editor.GetNode<VBoxContainer>("%ArmoryList").GetChildren()
      .AsValueEnumerable().OfType<Button>()
      .Single(button => button.Text.Contains("Rifle")).EmitSignal(Button.SignalName.Pressed);
    editor.GetNode<Button>("%BackButton").EmitSignal(Button.SignalName.Pressed);

    Assert.True(ReferenceEquals(squad, manager.Current)); // editor pops back to the squad
    var selected = squad.GetSelectedCombatants();
    Assert.Equal(1, selected.Count);
    Assert.Equal("Rifle", selected[0].EquippedWeapon.RequireSome().ItemName);

    squad.GetNode<Button>("%BackButton").EmitSignal(Button.SignalName.Pressed);

    // Preparation pops to the retained dialog instance with its existing buttons.
    Assert.True(ReferenceEquals(dialog, manager.Current));
    Assert.True(dialog.IsVisibleInTree());
    Assert.Equal(2, dialog.GetNode<HBoxContainer>("%Buttons").GetChildCount());

    string clockWhilePending = Clock(scene).Text;
    scene._PhysicsProcess(1.0); // the pending-resolution gate keeps root time frozen
    Assert.Equal(clockWhilePending, Clock(scene).Text);

    DialogButton(dialog, "Decline").EmitSignal(Button.SignalName.Pressed);

    Assert.True(ReferenceEquals(manager.RootView, manager.Current));
    Assert.True(dialog.GetParent() is null); // popped views detach immediately
    Assert.Equal(0, AlertCount(scene));
    await WaitForDeferredDeletion((SceneTree)Engine.GetMainLoop());
    Assert.False(GodotObject.IsInstanceValid(dialog));
  }

  [TestCase(TestName = "Re-engaging starts empty while equipment edits persist")]
  public async Task ReEngageStartsEmptyWithPersistedEquipment()
  {
    await using var cleanup = new DeferredNodeCleanup();
    var (scene, manager) = TacticalScene(
      [MakeEntry("Alpha")],
      armory: [MakeFirearmWeaponData("Rifle")]);
    OpenMissionViaAlert(scene);
    var dialog = (GeoscapeEventResolution)manager.Current;

    DialogButton(dialog, "Engage").EmitSignal(Button.SignalName.Pressed);
    var first = (SquadLoadoutView)manager.Current;
    ChooseIntoSlot(first, 1, "Alpha");
    first.GetNode<VBoxContainer>("%Slots").GetChild<Control>(1)
      .GetNode<Button>("%EditUnit").EmitSignal(Button.SignalName.Pressed);
    var editor = (UnitView)manager.Current;
    editor.SelectSlot(UnitViewSlot.Weapon);
    editor.GetNode<VBoxContainer>("%ArmoryList").GetChildren()
      .AsValueEnumerable().OfType<Button>()
      .Single(button => button.Text.Contains("Rifle")).EmitSignal(Button.SignalName.Pressed);
    editor.GetNode<Button>("%BackButton").EmitSignal(Button.SignalName.Pressed);
    first.GetNode<Button>("%BackButton").EmitSignal(Button.SignalName.Pressed);
    Assert.True(ReferenceEquals(dialog, manager.Current)); // back landed on the dialog

    DialogButton(dialog, "Engage").EmitSignal(Button.SignalName.Pressed); // re-engage

    var second = (SquadLoadoutView)manager.Current;
    Assert.False(ReferenceEquals(first, second)); // a fresh view each Engage
    Assert.Equal(0, second.GetSelectedCombatants().Count); // slots start empty
    Assert.Equal(3, second.GetNode<VBoxContainer>("%Slots").GetChildCount());
    Assert.Equal(2, dialog.GetNode<HBoxContainer>("%Buttons").GetChildCount()); // no growth

    // Choices start inert (no destination); Alpha is available again and keeps the rifle.
    Assert.True(second.GetNode<VBoxContainer>("%RosterChoices").GetChildren()
      .AsValueEnumerable().OfType<Button>().Single(button => button.Text.Contains("Alpha")).Disabled);
    ChooseIntoSlot(second, 0, "Alpha");
    Assert.True(second.GetSelectedCombatants()[0].EquippedWeapon.IsSome);
  }

  [TestCase(GeoscapeEventKind.Plot, "Continue",
    TestName = "Plot missions still resolve through Continue")]
  [TestCase(GeoscapeEventKind.Minigame, "Play (placeholder)",
    TestName = "Minigame missions still resolve through Play")]
  public async Task NonTacticalMissionsStillResolve(GeoscapeEventKind kind, string buttonText)
  {
    await using var cleanup = new DeferredNodeCleanup();
    var scene = AddToTree(CreateGeoscapeScene(MakeStart(
      timeline: [MakeScheduled(1, MakeEvent($"Story Beat {kind}", kind))])));
    var manager = scene.GetNode<GeoscapeViewManager>("%ViewManager");
    SpeedButton(scene).EmitSignal(Button.SignalName.Pressed);
    scene._PhysicsProcess(0.1);
    OpenMissionViaAlert(scene);
    var dialog = (GeoscapeEventResolution)manager.Current;

    DialogButton(dialog, buttonText).EmitSignal(Button.SignalName.Pressed);

    Assert.True(ReferenceEquals(manager.RootView, manager.Current));
    Assert.True(dialog.GetParent() is null);
    Assert.Equal(0, AlertCount(scene));
  }

  [TestCase(TestName = "Broken squad exports inside the scene preserve the pending mission")]
  public async Task BrokenSceneExportsPreserveThePendingMission()
  {
    await using var cleanup = new DeferredNodeCleanup();
    var (scene, manager) = TacticalScene([MakeEntry("Alpha")]);
    OpenMissionViaMarker(scene);
    var dialog = (GeoscapeEventResolution)manager.Current;

    // Engage's guard throws inside the pressed handler; Godot swallows it, so nothing
    // pushes and the dialog keeps presenting the still-pending mission.
    dialog.SquadViewScene = null;
    DialogButton(dialog, "Engage").EmitSignal(Button.SignalName.Pressed);
    Assert.True(ReferenceEquals(dialog, manager.Current));

    var probe = new Label { Name = "NotASquadLoadoutView" };
    var packed = new PackedScene();
    Error error = packed.Pack(probe);
    probe.Free();
    if (error != Error.Ok)
      throw new InvalidOperationException($"Test scene packing failed: {error}");
    dialog.SquadViewScene = packed;
    DialogButton(dialog, "Engage").EmitSignal(Button.SignalName.Pressed);
    Assert.True(ReferenceEquals(dialog, manager.Current));
    Assert.True(dialog.IsVisibleInTree());
    Assert.Equal(2, dialog.GetNode<HBoxContainer>("%Buttons").GetChildCount());
    Assert.Equal(1, AlertCount(scene));
  }
}
