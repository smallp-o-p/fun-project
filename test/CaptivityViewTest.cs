#nullable disable warnings
using System;
using System.Threading.Tasks;
using FunProject.Buffs;
using FunProject.Combatants;
using FunProject.Geoscape;
using FunProject.GameState;
using FunProject.Items;
using FunProject.Stats;
using FunProject.Weapons;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;
using static FunProject.Tests.GeoscapeTestScenes;

[TestSuite]
[RequireGodotRuntime]
public class CaptivityViewTest
{
  // One fixture per test: a campaign whose captivity holds the case's captives (identity
  // = live Combatant references) plus whatever roster the interrogation squad needs.
  private static GeoscapeFixture CaptiveCampaign(Combatant[] captives,
    RosterEntryData[] roster = null)
  {
    var fixture = new GeoscapeFixture(MakeStart(roster: roster));
    foreach (Combatant captive in captives)
      fixture.State.Captivity.Add(captive);
    return fixture;
  }

  private static Button Row(CaptivityView view, int index)
    => view.GetNode<VBoxContainer>("%CaptiveList").GetChild<Button>(index);

  // Drives a toggle the way the native control does: assigning ButtonPressed raises the
  // Toggled signal on the live row (no rebuild), the path a keyboard user's focus rides on.
  private static void ToggleRow(CaptivityView view, int index, bool pressed)
    => Row(view, index).ButtonPressed = pressed;

  [TestCase(TestName = "An empty captivity shows the empty state and keeps preparation inert")]
  public async Task EmptyCaptivityShowsEmptyStateAndKeepsPreparationInert()
  {
    await using var cleanup = new DeferredNodeCleanup();
    using var fixture = new GeoscapeFixture();
    var view = AddToTree(CreateCaptivityView());
    view.Present(fixture.State, fixture.Session);

    Assert.True(view.GetNode<Label>("%EmptyState").Visible);
    Assert.Equal(0, view.GetNode<VBoxContainer>("%CaptiveList").GetChildCount());
    Assert.Equal("Selected: 0", view.GetNode<Label>("%SelectionCount").Text);
    Assert.True(view.GetNode<Button>("%PrepareButton").Disabled);
    Assert.Equal("", view.GetNode<RichTextLabel>("%Details").Text);

    SysColGeneric.List<GeoscapeView> requested = [];
    view.ViewRequested += requested.Add;
    view.PrepareInterrogation(); // inert, matching the disabled control
    Assert.Equal(0, requested.Count);
  }

  [TestCase(TestName = "Same-name captives stay distinct rows with independent toggles")]
  public async Task SameNameCaptivesStayDistinctRowsWithIndependentToggles()
  {
    await using var cleanup = new DeferredNodeCleanup();
    var faction = MakeFaction("Cult");
    var first = MakeCombatant("Alpha", faction, aim: 60);
    var second = MakeCombatant("Alpha", faction, aim: 70);
    using var fixture = CaptiveCampaign([first, second]);
    var view = AddToTree(CreateCaptivityView());
    view.Present(fixture.State, fixture.Session);

    var list = view.GetNode<VBoxContainer>("%CaptiveList");
    Assert.Equal(2, list.GetChildCount()); // identity rows, not collapsed by name
    Assert.Equal("Alpha — Cult", Row(view, 0).Text);
    Assert.Equal("Alpha — Cult", Row(view, 1).Text);

    ToggleRow(view, 0, true);
    Assert.Equal("Selected: 1", view.GetNode<Label>("%SelectionCount").Text);
    Assert.True(Row(view, 0).ButtonPressed);
    Assert.False(Row(view, 1).ButtonPressed); // the sibling identity stayed unselected
    Assert.True(view.GetNode<RichTextLabel>("%Details").Text.Contains("Aim: 60"), // details inspect row 0's identity
      view.GetNode<RichTextLabel>("%Details").Text);

    ToggleRow(view, 1, true);
    Assert.Equal("Selected: 2", view.GetNode<Label>("%SelectionCount").Text);
    Assert.False(view.GetNode<Button>("%PrepareButton").Disabled);
    Assert.True(view.GetNode<RichTextLabel>("%Details").Text.Contains("Aim: 70"));

    // The outgoing heading carries both identities in displayed (captivity) order.
    GeoscapeView? produced = null;
    view.ViewRequested += requested => produced = requested;
    view.PrepareInterrogation();
    var squad = AddToTree((SquadLoadoutView)produced!);
    squad.Present(fixture.State, fixture.Session);
    Assert.Equal("Interrogation",
      squad.GetNode<Label>("%Title").Text);

    ToggleRow(view, 0, false); // deselection
    Assert.Equal("Selected: 1", view.GetNode<Label>("%SelectionCount").Text);
    Assert.False(Row(view, 0).ButtonPressed);
    Assert.True(Row(view, 1).ButtonPressed);
  }

  [TestCase(TestName = "Toggling a row updates the selection in place and keeps keyboard focus")]
  public async Task TogglingARowUpdatesSelectionInPlaceAndKeepsKeyboardFocus()
  {
    await using var cleanup = new DeferredNodeCleanup();
    var first = MakeCombatant("Alpha", MakeFaction("Cult"));
    var second = MakeCombatant("Bravo", MakeFaction("Cult"));
    using var fixture = CaptiveCampaign([first, second]);
    var view = AddToTree(CreateCaptivityView());
    view.Present(fixture.State, fixture.Session);

    var row = Row(view, 0);
    row.GrabFocus();
    Assert.True(row.HasFocus());

    row.ButtonPressed = true; // native toggle: the row must survive the handler

    Assert.True(ReferenceEquals(row, Row(view, 0))); // no rebuild destroyed the focused row
    Assert.True(row.HasFocus()); // keyboard focus survived the toggle
    Assert.True(row.ButtonPressed);
    Assert.Equal("Selected: 1", view.GetNode<Label>("%SelectionCount").Text);
    Assert.True(view.GetNode<RichTextLabel>("%Details").Text.Contains("Alpha"));
  }

  [TestCase(TestName = "Details inspect the first captive, then whichever row is toggled")]
  public async Task DetailsInspectFirstCaptiveThenToggledRows()
  {
    await using var cleanup = new DeferredNodeCleanup();
    var cult = MakeFaction("Cult of Sirius");
    var frenzy = MakeBuff("Frenzy", new HealthBelowPercentCondition { Percent = 200f });
    var equipped = MakeCombatant("Alpha", cult, health: 20, actionPoints: 8, will: 55,
      movement: 14, vision: 22, aim: 60, buffs: [frenzy]);
    var rifle = new FirearmWeapon(MakeFirearmWeaponData("Plasma Rifle", modSlots: 1));
    rifle.GetModSlots()[0].Equip(new MultiStatMod
    { StatMods = [new AimStatMod { Modifiers = [StatModifier.Add(10)] }] });
    equipped.EquipWeapon(rifle);
    equipped.EquipArmor(MakeArmor("Vest"));
    equipped.EquipItem(new EquippableItem(MakeItemData("Medkit")), 0);
    var bare = MakeCombatant("Bare", MakeFaction("Hybrids"));
    using var fixture = CaptiveCampaign([equipped, bare]);
    var view = AddToTree(CreateCaptivityView());
    view.Present(fixture.State, fixture.Session);

    string details = view.GetNode<RichTextLabel>("%Details").Text; // first captive initially
    Assert.True(details.Contains("Name: Alpha"), details);
    Assert.True(details.Contains("Faction: Cult of Sirius"), details);
    Assert.True(details.Contains("Health (max): 20"), details);
    Assert.True(details.Contains("Action Points: 8"), details);
    Assert.True(details.Contains("Will: 55"), details);
    Assert.True(details.Contains("Movement: 14"), details);
    Assert.True(details.Contains("Vision: 22"), details);
    Assert.True(details.Contains("Aim: 70"), details); // weapon mod contribution folded in
    Assert.True(details.Contains("Weapon: Plasma Rifle"), details);
    Assert.True(details.Contains("Armor: Vest"), details);
    Assert.True(details.Contains("Utility 1: Medkit"), details);
    Assert.True(details.Contains("Buffs: Frenzy"), details);

    ToggleRow(view, 1, true); // toggling inspects that captive
    details = view.GetNode<RichTextLabel>("%Details").Text;
    Assert.True(details.Contains("Name: Bare"), details);
    Assert.True(details.Contains("Weapon: — empty —"), details);
    Assert.True(details.Contains("Armor: — empty —"), details);
    Assert.True(details.Contains("Buffs: — none —"), details);
  }

  [TestCase(TestName = "Refresh retains selections; rebinding to another state drops stale ones")]
  public async Task RefreshRetainsSelectionsAndRebindingDropsStaleOnes()
  {
    await using var cleanup = new DeferredNodeCleanup();
    var faction = MakeFaction("Cult");
    var first = MakeCombatant("Alpha", faction);
    var second = MakeCombatant("Bravo", faction);
    using var fixture = CaptiveCampaign([first, second]);
    var view = AddToTree(CreateCaptivityView());
    view.Present(fixture.State, fixture.Session);
    ToggleRow(view, 1, true); // select Bravo; details show Bravo

    view.Present(fixture.State, fixture.Session); // same state: selection retained
    Assert.Equal(2, view.GetNode<VBoxContainer>("%CaptiveList").GetChildCount());
    Assert.True(Row(view, 1).ButtonPressed);
    Assert.False(Row(view, 0).ButtonPressed);
    Assert.Equal("Selected: 1", view.GetNode<Label>("%SelectionCount").Text);
    Assert.True(view.GetNode<RichTextLabel>("%Details").Text.Contains("Bravo"));

    using var other = CaptiveCampaign([MakeCombatant("Nova", MakeFaction("Sectoids"))]);
    view.Present(other.State, other.Session); // rebound: stale selection/details removed
    Assert.Equal("Selected: 0", view.GetNode<Label>("%SelectionCount").Text);
    Assert.False(Row(view, 0).ButtonPressed);
    Assert.True(view.GetNode<Button>("%PrepareButton").Disabled);
    Assert.True(view.GetNode<RichTextLabel>("%Details").Text.Contains("Nova"));
    Assert.False(view.GetNode<RichTextLabel>("%Details").Text.Contains("Bravo"));
  }

  [TestCase(TestName = "Prepare interrogation opens a two-slot read-only squad view")]
  public async Task PrepareInterrogationOpensATwoSlotReadOnlySquadView()
  {
    await using var cleanup = new DeferredNodeCleanup();
    var first = MakeCombatant("Alpha", MakeFaction("Cult of Sirius"));
    var second = MakeCombatant("Bravo", MakeFaction("Hybrids"));
    using var fixture = CaptiveCampaign([first, second]);
    var view = AddToTree(CreateCaptivityView());
    view.Present(fixture.State, fixture.Session);
    ToggleRow(view, 0, true);
    ToggleRow(view, 1, true);

    GeoscapeView? produced = null;
    view.ViewRequested += requested => produced = requested;
    view.GetNode<Button>("%PrepareButton").EmitSignal(Button.SignalName.Pressed);

    var squad = AddToTree((SquadLoadoutView)produced!);
    squad.Present(fixture.State, fixture.Session); // materializes the handed-over configuration
    string title = squad.GetNode<Label>("%Title").Text;
    Assert.Equal(title, squad.GetNode<Label>("%Title").TooltipText); // inspectable when clipped
    Assert.Equal(2, squad.GetNode<BoxContainer>("%Slots").GetChildCount());
    Assert.Equal("Squad: 0/2", squad.GetNode<Label>("%SquadCount").Text);
  }

  [TestCase(false, TestName = "Interrogation preparation leaves campaign truth untouched")]
  [TestCase(true, TestName = "Interrogation squad enforces two distinct roster units")]
  public async Task InterrogationPreparationSelectsDistinctRosterWithoutCampaignEffects(bool secondInterrogator)
  {
    await using var cleanup = new DeferredNodeCleanup();
    var captive = MakeCombatant("Alpha", MakeFaction("Cult"));
    var roster = secondInterrogator
      ? new RosterEntryData[] { MakeEntry("Scout"), MakeEntry("Medic") }
      : new RosterEntryData[] { MakeEntry("Scout") };
    using var fixture = CaptiveCampaign([captive], roster);
    var view = AddToTree(CreateCaptivityView());
    view.Present(fixture.State, fixture.Session);
    ToggleRow(view, 0, true);

    GeoscapeView? produced = null;
    view.ViewRequested += requested => produced = requested;
    view.PrepareInterrogation();
    var squad = AddToTree((SquadLoadoutView)produced!);
    squad.Present(fixture.State, fixture.Session);
    ChooseSquadUnit(squad, 0, "Scout");

    Control card = SquadSlot(squad, 0);
    Assert.False(card.GetNode<Button>("%EditUnit").Visible); // editing hidden while occupied

    Assert.True(Row(view, 0).ButtonPressed); // captive selection untouched
    Assert.Equal("Selected: 1", view.GetNode<Label>("%SelectionCount").Text);

    // Captivity, faction, roster, and session truth survive preparation untouched.
    Assert.Equal(1, fixture.State.Captivity.Combatants.Count);
    Assert.True(ReferenceEquals(captive, fixture.State.Captivity.Combatants[0]));
    Assert.Equal("Cult", fixture.State.Captivity.Combatants[0].OwningFaction.Name);
    Assert.True(fixture.State.Roster[0].EquippedWeapon.IsNone); // nothing moved to the Armory
    Assert.Equal(0, fixture.Events.Count); // no session lifecycle was committed

    if (secondInterrogator)
    {
      squad.GetNode<BoxContainer>("%Slots").GetChild<Control>(1)
        .GetNode<Button>("%ChooseUnit").EmitSignal(Button.SignalName.Pressed); // mark slot 2
      Assert.True(SquadChoice(squad, "Scout").Disabled); // occupied units stay unavailable
      Assert.False(SquadChoice(squad, "Medic").Disabled);

      ChooseSquadUnit(squad, 1, "Medic"); // the open destination takes the second distinct unit
      var selected = squad.GetSelectedCombatants();
      Assert.Equal(2, selected.Count);
      Assert.True(ReferenceEquals(fixture.State.Roster[0], selected[0]));
      Assert.True(ReferenceEquals(fixture.State.Roster[1], selected[1]));
    }
    else
    {
      var selected = squad.GetSelectedCombatants();
      Assert.Equal(1, selected.Count);
      Assert.True(ReferenceEquals(fixture.State.Roster[0], selected[0]));
    }
  }

  [TestCase(TestName = "Back from interrogation returns to the retained captivity selection")]
  public async Task BackFromInterrogationReturnsToRetainedSelection()
  {
    await using var cleanup = new DeferredNodeCleanup();
    var first = MakeCombatant("Alpha", MakeFaction("Cult"));
    var second = MakeCombatant("Bravo", MakeFaction("Cult"));
    using var fixture = CaptiveCampaign([first, second], [MakeEntry("Scout")]);
    var manager = CreatePresentedManager(fixture);

    var captivity = CreateCaptivityView();
    manager.Push(captivity); // ViewChanged presents
    ToggleRow(captivity, 0, true);
    ToggleRow(captivity, 1, true);

    captivity.GetNode<Button>("%PrepareButton").EmitSignal(Button.SignalName.Pressed);
    var squad = (SquadLoadoutView)manager.Current;
    Assert.False(captivity.IsVisibleInTree()); // covered but retained
    squad.GetNode<Button>("%BackButton").EmitSignal(Button.SignalName.Pressed);

    Assert.True(ReferenceEquals(captivity, manager.Current)); // back landed on captivity
    Assert.True(captivity.IsVisibleInTree());
    Assert.Equal(2, captivity.GetNode<VBoxContainer>("%CaptiveList").GetChildCount());
    Assert.True(Row(captivity, 0).ButtonPressed); // selection intact after re-present
    Assert.True(Row(captivity, 1).ButtonPressed);
    Assert.Equal("Selected: 2", captivity.GetNode<Label>("%SelectionCount").Text);

    ToggleRow(captivity, 0, false); // returning must retain an accessible way to deselect
    Assert.Equal("Selected: 1", captivity.GetNode<Label>("%SelectionCount").Text);
    Assert.False(Row(captivity, 0).ButtonPressed);
    Assert.True(Row(captivity, 1).ButtonPressed);
  }

  [TestCase(TestName = "The HUD captivity button forwards a fresh CaptivityView")]
  public async Task HudCaptivityButtonForwardsAFreshCaptivityView()
  {
    await using var cleanup = new DeferredNodeCleanup();
    var hud = AddToTree(CreateHud());

    SysColGeneric.List<GeoscapeView> received = [];
    // Emitted views are live and unparented; register each for cleanup or they leak as orphans.
    hud.ViewRequested += view =>
    {
      AutoFree(view);
      received.Add(view);
    };
    hud.GetNode<Button>("%CaptivityButton").EmitSignal(Button.SignalName.Pressed);

    Assert.Equal(1, received.Count);
    Assert.True(received[0] is CaptivityView);
    Assert.True(received[0].GetParent() is null); // emitted live and unparented
  }

  [TestCase(TestName = "Missing and wrong-root SquadViewScene are authoring errors")]
  public async Task MissingAndWrongRootSquadViewSceneAreAuthoringErrors()
  {
    await using var cleanup = new DeferredNodeCleanup();
    var captive = MakeCombatant("Alpha", MakeFaction("Cult"));
    using var fixture = CaptiveCampaign([captive]);
    var view = AddToTree(CreateCaptivityView());
    view.Present(fixture.State, fixture.Session);
    ToggleRow(view, 0, true);

    SysColGeneric.List<GeoscapeView> requested = [];
    view.ViewRequested += requested.Add;

    view.SquadViewScene = Pack(new Label { Name = "NotASquadLoadoutView" });

    // Free (not QueueFree) leaves no orphan behind the synchronous throw.
    long orphansBefore = (long)Performance.Singleton.GetMonitor(Performance.Monitor.ObjectOrphanNodeCount);
    Assert.Throws<InvalidOperationException>(() => view.PrepareInterrogation());
    Assert.Equal(orphansBefore,
      (long)Performance.Singleton.GetMonitor(Performance.Monitor.ObjectOrphanNodeCount));

    Assert.Equal(0, requested.Count);
    Assert.True(Row(view, 0).ButtonPressed); // selection untouched
    Assert.Equal("Selected: 1", view.GetNode<Label>("%SelectionCount").Text);
  }
}
