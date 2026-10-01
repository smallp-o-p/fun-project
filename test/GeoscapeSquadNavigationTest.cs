#nullable disable warnings
using System.Threading.Tasks;
using FunProject.Combatants;
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
  // un-pauses to 5x; a single physics step fires the mission. Opening the mission
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

  private static Label Clock(GeoscapeScene scene)
    => scene.GetNode<GeoscapeHud>("%GeoscapeHud").GetNode<Label>("%ClockLabel");

  private static Button DialogButton(GeoscapeEventResolution dialog, string text)
    => dialog.GetNode<HBoxContainer>("%Buttons").GetChildren()
      .AsValueEnumerable().OfType<Button>()
      .Single(button => button.Text == text);

  // Drives the real dialog's Engage handover and returns the presented squad view it
  // produced, so a test can assert the opener forwarded the pending mission's policy.
  private static SquadLoadoutView EngagedSquadView(GeoscapeFixture fixture)
  {
    var dialog = AddToTree(CreateResolutionView());
    dialog.Present(fixture.State, fixture.Session);
    GeoscapeView? produced = null;
    dialog.ViewRequested += view => produced = view;
    DialogButton(dialog, "Engage").EmitSignal(Button.SignalName.Pressed);
    var squad = AddToTree((SquadLoadoutView)produced!);
    squad.Present(fixture.State, fixture.Session); // materializes the handed-over configuration
    return squad;
  }

  [TestCase("Operation", TestName = "Engage produces a squad view without resolving the mission")]
  [TestCase("Operation Iron", TestName = "Engage produces a squad view configured with the mission title")]
  public async Task EngageProducesConfiguredSquadWithoutResolving(string title)
  {
    await using var cleanup = new DeferredNodeCleanup();
    using var fixture = GeoscapeFixture.WithFiredEvent(
      MakeEvent(title, GeoscapeEventKind.TacticalBattle));
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

    var squad = AddToTree((SquadLoadoutView)produced);
    squad.Present(fixture.State, fixture.Session); // materializes the handed-over configuration
    Assert.Equal(title, squad.GetNode<Label>("%Title").Text);
    Assert.Equal("Squad: 0/3", squad.GetNode<Label>("%SquadCount").Text); // default three slots
    Assert.Equal(0, squad.GetSelectedCombatants().Count);
    Assert.Equal(0, fixture.Events.Count); // still no resolution lifecycle
  }

  [TestCase(false, "Operation Iron", TestName = "Engage forwards the mission's policy: injured units stay blocked")]
  [TestCase(true, "Last Stand", TestName = "Engage forwards an override mission: unfit units deploy with notice")]
  public async Task EngageForwardsMissionPolicy(bool allowUnfitDeployment, string title)
  {
    await using var cleanup = new DeferredNodeCleanup();
    using var fixture = new GeoscapeFixture(MakeStart(
      roster: [MakeConditionEntry("Alpha")],
      timeline: [MakeScheduled(1,
        MakeEvent(title, GeoscapeEventKind.TacticalBattle, allowUnfitDeployment: allowUnfitDeployment))]));
    fixture.AdvanceTicks(1);
    fixture.OpenResolution(fixture.ActiveEvent);
    Combatant alpha = fixture.State.Roster[0];
    fixture.ReturnFromMission((alpha, 25, 0)); // Injured + Tired: barred from the mission itself

    var squad = EngagedSquadView(fixture);

    string prompt = squad.GetNode<Label>("%SelectionPrompt").Text;
    Assert.True(allowUnfitDeployment
      ? prompt.Contains("Exceptional deployment")
      : !prompt.Contains("Exceptional"));
    squad.GetNode<BoxContainer>("%Slots").GetChild<Control>(0)
      .GetNode<Button>("%ChooseUnit").EmitSignal(Button.SignalName.Pressed);
    Assert.Equal(!allowUnfitDeployment, SquadChoice(squad, "Alpha").Disabled); // the opener's mission context
    if (!allowUnfitDeployment)
      return;

    SquadChoice(squad, "Alpha").EmitSignal(BaseButton.SignalName.Pressed); // the override authorized the unfit unit
    var selected = squad.GetSelectedCombatants();
    Assert.Equal(1, selected.Count);
    Assert.True(ReferenceEquals(alpha, selected[0]));
  }

  [TestCase(false, true, 0, false,
    TestName = "Full round trip keeps the mission and Decline still resolves")]
  [TestCase(false, false, 1, true,
    TestName = "Re-engaging starts empty while equipment edits persist")]
  [TestCase(true, false, 0, false,
    TestName = "Engage covers the dialog with squad preparation")]
  public async Task PreparationRoundTrip(bool preparationOnly, bool bravoInRoster, int selectedSlot, bool reEngage)
  {
    await using var cleanup = new DeferredNodeCleanup();
    var roster = bravoInRoster
      ? new[] { MakeEntry("Alpha"), MakeEntry("Bravo") }
      : new[] { MakeEntry("Alpha") };
    var (scene, manager) = preparationOnly
      ? TacticalScene(roster)
      : TacticalScene(roster, armory: [MakeFirearmWeaponData("Rifle")]);
    OpenResolutionViaMapEvent(scene);
    var dialog = (GeoscapeEventResolution)manager.Current;

    DialogButton(dialog, "Engage").EmitSignal(Button.SignalName.Pressed);
    var squad = (SquadLoadoutView)manager.Current;
    Assert.Equal("Operation Iron", squad.GetNode<Label>("%Title").Text);
    Assert.False(dialog.IsVisibleInTree()); // squad preparation covers the dialog
    Assert.True(squad.IsVisibleInTree());
    string clock = Clock(scene).Text;
    scene._PhysicsProcess(1.0); // covered views freeze the clock at the composition root
    Assert.Equal(clock, Clock(scene).Text);
    Assert.Equal(1, MapEventMarkers(scene).Length); // the mission stays active: its marker remains
    if (preparationOnly)
      return;

    ChooseSquadUnit(squad, selectedSlot, "Alpha");
    squad.GetNode<BoxContainer>("%Slots").GetChild<Control>(selectedSlot)
      .GetNode<Button>("%EditUnit").EmitSignal(Button.SignalName.Pressed);
    var editor = (UnitView)manager.Current;
    Assert.False(squad.IsVisibleInTree()); // the nested editor covers preparation
    editor.SelectSlot(UnitViewSlot.Weapon);
    ArmoryButton(editor, "Rifle").EmitSignal(Button.SignalName.Pressed);
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

    if (reEngage)
    {
      DialogButton(dialog, "Engage").EmitSignal(Button.SignalName.Pressed); // re-engage

      var second = (SquadLoadoutView)manager.Current;
      Assert.False(ReferenceEquals(squad, second)); // a fresh view each Engage
      Assert.Equal(0, second.GetSelectedCombatants().Count); // slots start empty
      Assert.Equal(3, second.GetNode<BoxContainer>("%Slots").GetChildCount());
      Assert.Equal(2, dialog.GetNode<HBoxContainer>("%Buttons").GetChildCount()); // no growth

      // Choices start inert (no destination); Alpha is available again and keeps the rifle.
      Assert.True(SquadChoice(second, "Alpha").Disabled);
      ChooseSquadUnit(second, 0, "Alpha");
      Assert.True(second.GetSelectedCombatants()[0].EquippedWeapon.IsSome);

      second.GetNode<Button>("%BackButton").EmitSignal(Button.SignalName.Pressed);
      Assert.True(ReferenceEquals(dialog, manager.Current)); // popped back to the dialog
    }

    DialogButton(dialog, "Decline").EmitSignal(Button.SignalName.Pressed);

    Assert.True(ReferenceEquals(manager.RootView, manager.Current));
    Assert.True(dialog.GetParent() is null); // popped views detach immediately
    Assert.Equal(0, MapEventMarkers(scene).Length);
    await WaitForDeferredDeletion((SceneTree)Engine.GetMainLoop());
    Assert.False(GodotObject.IsInstanceValid(dialog));
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
    OpenResolutionViaMapEvent(scene);
    var dialog = (GeoscapeEventResolution)manager.Current;

    DialogButton(dialog, buttonText).EmitSignal(Button.SignalName.Pressed);

    Assert.True(ReferenceEquals(manager.RootView, manager.Current));
    Assert.True(dialog.GetParent() is null);
    Assert.Equal(0, MapEventMarkers(scene).Length);
  }

  [TestCase(TestName = "Broken squad exports inside the scene preserve the pending mission")]
  public async Task BrokenSceneExportsPreserveThePendingMission()
  {
    await using var cleanup = new DeferredNodeCleanup();
    var (scene, manager) = TacticalScene([MakeEntry("Alpha")]);
    OpenResolutionViaMapEvent(scene);
    var dialog = (GeoscapeEventResolution)manager.Current;

    // Engage's guard throws inside the pressed handler; Godot swallows it, so nothing
    // pushes and the dialog keeps presenting the still-pending mission.
    dialog.SquadViewScene = null;
    DialogButton(dialog, "Engage").EmitSignal(Button.SignalName.Pressed);
    Assert.True(ReferenceEquals(dialog, manager.Current));

    dialog.SquadViewScene = Pack(new Label { Name = "NotASquadLoadoutView" });
    DialogButton(dialog, "Engage").EmitSignal(Button.SignalName.Pressed);
    Assert.True(ReferenceEquals(dialog, manager.Current));
    Assert.True(dialog.IsVisibleInTree());
    Assert.Equal(2, dialog.GetNode<HBoxContainer>("%Buttons").GetChildCount());
    Assert.Equal(1, MapEventMarkers(scene).Length);
  }
}
