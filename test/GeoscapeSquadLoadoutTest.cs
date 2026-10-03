#nullable disable warnings
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FunProject.Combatants;
using FunProject.Geoscape;
using FunProject.GameState;
using FunProject.Items;
using FunProject.Stats;
using FunProject.Strategic;
using FunProject.Weapons;
using Godot;
using GdUnit4;
using CampaignGameState = global::FunProject.GameState.GameState;
using static FunProject.Tests.GeoscapeTestScenes;

[TestSuite]
[RequireGodotRuntime]
public class GeoscapeSquadLoadoutTest
{
  // One fixture per test: a pending tactical mission plus whatever campaign roster/armory
  // the case needs. The mission fires at tick 1; AdvanceTicks both fires and freezes time
  // behind the pending resolution. The optional mission definition substitutes the event
  // (e.g. an unfit-deployment override).
  private static (GeoscapeFixture Fixture, PendingResolution Pending) Mission(
    RosterEntryData[] roster,
    EquippableItemData[] armory = null,
    EquippableMod[] modStock = null,
    GeoscapeEventDefinition mission = null)
  {
    var fixture = new GeoscapeFixture(MakeStart(
      roster: roster,
      timeline: [MakeScheduled(1, mission ?? MakeEvent("Operation Iron", GeoscapeEventKind.TacticalBattle))],
      armory: armory,
      modStock: modStock));
    fixture.AdvanceTicks(1);
    fixture.OpenResolution(fixture.ActiveEvent);
    return (fixture, fixture.Session.PendingResolution.RequireSome("Expected pending mission."));
  }

  private static SquadLoadoutView PresentedSquadView(PendingResolution pending,
    CampaignGameState state, GeoscapeSession session)
  {
    var view = AddToTree(CreateSquadLoadoutView());
    // The opener supplies the mission configuration: title, three editable slots, and the
    // pending mission itself as explicit deployment context.
    view.Configure(pending.Event.Definition.Title, 3, allowEquipmentEditing: true,
      mission: pending.Event.Definition);
    view.Present(state, session);
    return view;
  }

  // Mission preparation as the resolution opener now performs it: the entire active event
  // handed to ConfigureMission, which derives title, gate, and capacity from it.
  private static SquadLoadoutView PresentedMissionView(PendingResolution pending,
    CampaignGameState state, GeoscapeSession session)
  {
    var view = AddToTree(CreateSquadLoadoutView());
    view.ConfigureMission(pending.Event);
    view.Present(state, session);
    return view;
  }

  private static void Press(params BaseButton[] buttons)
  {
    foreach (BaseButton button in buttons)
      button.EmitSignal(BaseButton.SignalName.Pressed);
  }

  private static string CardText(Control card)
    => $"{card.GetNode<Label>("%CardTitle").Text}\n"
      + $"{card.GetNode<RichTextLabel>("%Equipment").Text}\n"
      + $"{card.GetNode<RichTextLabel>("%Stats").Text}";

  [TestCase(TestName = "Configure resizes slots and resets picks")]
  public async Task ConfigureResizesSlotsAndResetsPicks()
  {
    await using var cleanup = new DeferredNodeCleanup();
    var (fixture, pending) = Mission([MakeEntry("Alpha"), MakeEntry("Bravo")]);
    using var _ = fixture;
    var view = PresentedSquadView(pending, fixture.State, fixture.Session);
    ChooseSquadUnit(view, 0, "Alpha");

    view.Configure("Interrogation prep", 2, allowEquipmentEditing: true);
    view.Present(fixture.State, fixture.Session);

    Assert.Equal(2, view.GetNode<BoxContainer>("%Slots").GetChildCount());
    Assert.Equal("Squad: 0/2", view.GetNode<Label>("%SquadCount").Text); // picks were reset
    Assert.Equal(0, view.GetSelectedCombatants().Count);
    Assert.Equal("Interrogation prep", view.GetNode<Label>("%Title").Text);
    Assert.Equal("Interrogation prep", view.GetNode<Label>("%Title").TooltipText);

    ChooseSquadUnit(view, 0, "Alpha");
    ChooseSquadUnit(view, 1, "Bravo");
    var selected = view.GetSelectedCombatants();
    Assert.Equal(2, selected.Count);
    Assert.True(ReferenceEquals(fixture.State.Roster[0], selected[0]));
    Assert.True(ReferenceEquals(fixture.State.Roster[1], selected[1]));
    Assert.Equal("Squad: 2/2", view.GetNode<Label>("%SquadCount").Text);
  }

  [TestCase(TestName = "Editing-disabled configuration hides Edit on occupied cards")]
  public async Task EditingDisabledConfigurationHidesEdit()
  {
    await using var cleanup = new DeferredNodeCleanup();
    var (fixture, pending) = Mission([MakeEntry("Alpha")]);
    using var _ = fixture;
    var view = AddToTree(CreateSquadLoadoutView());
    view.Configure("Interrogation — Alpha (Cult)", 2, allowEquipmentEditing: false);
    view.Present(fixture.State, fixture.Session);
    ChooseSquadUnit(view, 0, "Alpha");

    Control card = SquadSlot(view, 0);
    Assert.False(card.GetNode<Button>("%EditUnit").Visible); // hidden on an occupied card
    Assert.True(card.GetNode<Button>("%RemoveUnit").Visible); // squad picks still work
    Assert.Equal(1, view.GetSelectedCombatants().Count);
  }

  [TestCase(TestName = "An empty-roster mission presents three empty slots and no choices")]
  public async Task EmptyRosterPresentsThreeEmptySlots()
  {
    await using var cleanup = new DeferredNodeCleanup();
    var (fixture, pending) = Mission([]);
    using var _ = fixture;

    var view = PresentedSquadView(pending, fixture.State, fixture.Session);

    Assert.Equal("Operation Iron", view.GetNode<Label>("%Title").Text);
    Assert.Equal("Squad: 0/3", view.GetNode<Label>("%SquadCount").Text);
    Assert.Equal(3, view.GetNode<BoxContainer>("%Slots").GetChildCount());
    Assert.Equal(0, view.GetSelectedCombatants().Count);

    var choiceText = view.GetNode<VBoxContainer>("%RosterChoices")
      .GetChild(0) is Label note ? note.Text : "";
    Assert.True(choiceText.Contains("No units"), $"Expected an empty-roster note, got '{choiceText}'.");
    foreach (Node card in view.GetNode<BoxContainer>("%Slots").GetChildren())
    {
      var text = CardText((Control)card);
      Assert.True(text.Contains("empty"), $"Expected an empty slot card, got '{text}'.");
    }
  }

  [TestCase(TestName = "The squad scene inherits the shared geoscape view shell")]
  public void SceneInheritsTheSharedShell()
  {
    var scene = GD.Load<PackedScene>("res://scenes/geoscape/squad/SquadLoadoutView.tscn");

    SceneState? baseState = scene.GetState().GetBaseSceneState();

    Assert.True(baseState is not null, "SquadLoadoutView.tscn must inherit the shared shell scene.");
    Assert.Equal("res://scenes/geoscape/GeoscapeView.tscn", baseState!.GetPath());
  }

  [TestCase(TestName = "The authored title passes the mouse through so its tooltip is reachable")]
  public void AuthoredTitlePassesMouseThroughForTooltip()
  {
    var view = GD.Load<PackedScene>("res://scenes/geoscape/squad/SquadLoadoutView.tscn")
      .Instantiate<SquadLoadoutView>();
    try
    {
      // The label clips long target lists; MouseFilter.Pass keeps its tooltip hoverable.
      Assert.Equal(Control.MouseFilterEnum.Pass, view.GetNode<Label>("%Title").MouseFilter);
    }
    finally
    {
      view.Free(); // never entered the tree; keep the orphan monitor clean
    }
  }

  [TestCase(TestName = "Mission size controls preparation capacity and Deploy readiness")]
  public async Task MissionSizeControlsPreparationCapacity()
  {
    await using var cleanup = new DeferredNodeCleanup();
    var (fixture, pending) = Mission([MakeEntry("Alpha")],
      mission: MakeEvent("Operation Iron", GeoscapeEventKind.TacticalBattle,
        tacticalMission: MakeTacticalMission(maxPlayerUnits: 1)));
    using var _ = fixture;
    var squad = PresentedMissionView(pending, fixture.State, fixture.Session);

    Assert.Equal(1, squad.GetNode<BoxContainer>("%Slots").GetChildCount());
    Assert.Equal("Squad: 0/1", squad.GetNode<Label>("%SquadCount").Text);
    Assert.True(SquadDeployButton(squad).Visible); // configured tactical mission
    Assert.True(SquadDeployButton(squad).Disabled); // empty selection
    ChooseSquadUnit(squad, 0, "Alpha");
    Assert.False(SquadDeployButton(squad).Disabled);
  }

  [TestCase(TestName = "Deploy carries the configured mission and selected identities across reopen")]
  public async Task DeployCarriesMissionAndSelectedIdentities()
  {
    await using var cleanup = new DeferredNodeCleanup();
    var (fixture, pending) = Mission([MakeEntry("Alpha"), MakeEntry("Bravo")]);
    using var _ = fixture;
    int seed = pending.Event.BattleSeed.RequireSome();
    var squad = PresentedMissionView(pending, fixture.State, fixture.Session);
    ChooseSquadUnit(squad, 0, "Alpha");
    ChooseSquadUnit(squad, 1, "Bravo");

    var requested = new SysColGeneric.List<(GeoscapeEvent Mission, IReadOnlyList<Combatant> Units)>();
    squad.DeployRequested += (mission, units) => requested.Add((mission, units));
    SquadDeployButton(squad).EmitSignal(Button.SignalName.Pressed);

    Assert.Equal(1, requested.Count);
    Assert.True(ReferenceEquals(pending.Event, requested[0].Mission),
      "Deploy carries the exact retained event, not a stale copy.");
    Assert.Equal(seed, requested[0].Mission.BattleSeed.RequireSome());
    Assert.Equal(2, requested[0].Units.Count);
    Assert.True(ReferenceEquals(fixture.State.Roster[0], requested[0].Units[0]));
    Assert.True(ReferenceEquals(fixture.State.Roster[1], requested[0].Units[1]));

    // Back/reopen re-prepares from the same retained event with the same seed.
    squad.ConfigureMission(pending.Event);
    squad.Present(fixture.State, fixture.Session);
    ChooseSquadUnit(squad, 0, "Alpha");
    var reopened = new SysColGeneric.List<(GeoscapeEvent Mission, IReadOnlyList<Combatant> Units)>();
    squad.DeployRequested += (mission, units) => reopened.Add((mission, units));
    SquadDeployButton(squad).EmitSignal(Button.SignalName.Pressed);

    Assert.Equal(1, reopened.Count);
    Assert.True(ReferenceEquals(pending.Event, reopened[0].Mission));
    Assert.Equal(seed, reopened[0].Mission.BattleSeed.RequireSome());
  }

  [TestCase(TestName = "Deployment failures render without dropping the selection")]
  public async Task DeploymentFailuresRenderWithoutDroppingSelection()
  {
    await using var cleanup = new DeferredNodeCleanup();
    var (fixture, pending) = Mission([MakeEntry("Alpha")]);
    using var _ = fixture;
    var squad = PresentedMissionView(pending, fixture.State, fixture.Session);
    ChooseSquadUnit(squad, 0, "Alpha");

    var failure = new MissionLaunchFailure(
      MissionLaunchFailureReason.UnitUnavailable, "Alpha is not fit to deploy on this mission.");
    squad.ShowDeploymentFailure(failure);

    var label = SquadDeploymentFailure(squad);
    Assert.True(label.Visible);
    Assert.Equal("Alpha is not fit to deploy on this mission.", label.Text);
    var selected = squad.GetSelectedCombatants();
    Assert.Equal(1, selected.Count);
    Assert.True(ReferenceEquals(fixture.State.Roster[0], selected[0]));
    Assert.False(SquadDeployButton(squad).Disabled); // the squad stays launchable
  }

  [TestCase(TestName = "Generic configuration clears the deploy context")]
  public async Task GenericConfigurationClearsDeployContext()
  {
    await using var cleanup = new DeferredNodeCleanup();
    var (fixture, pending) = Mission([MakeEntry("Alpha")]);
    using var _ = fixture;
    var squad = PresentedMissionView(pending, fixture.State, fixture.Session);
    Assert.True(SquadDeployButton(squad).Visible); // mission prep offers Deploy

    squad.Configure("Interrogation", 2, allowEquipmentEditing: false);
    squad.Present(fixture.State, fixture.Session);

    Assert.False(SquadDeployButton(squad).Visible);
    int requested = 0;
    squad.DeployRequested += (_, _) => requested++;
    SquadDeployButton(squad).EmitSignal(Button.SignalName.Pressed); // hidden stays inert
    Assert.Equal(0, requested);
  }

  [TestCase(TestName = "Choices are inert without a destination and occupied units stay unavailable")]
  public async Task ChoicesRequireADestinationAndExcludeOccupiedUnits()
  {
    await using var cleanup = new DeferredNodeCleanup();
    var (fixture, pending) = Mission(
      [MakeEntry("Alpha"), MakeEntry("Bravo"), MakeEntry("Charlie")]);
    using var _ = fixture;
    var view = PresentedSquadView(pending, fixture.State, fixture.Session);

    Assert.True(SquadChoice(view, "Alpha").Disabled); // no destination yet
    Assert.True(SquadChoice(view, "Bravo").Disabled);

    ChooseSquadUnit(view, 1, "Alpha");

    var selected = view.GetSelectedCombatants();
    Assert.Equal(1, selected.Count);
    Assert.True(ReferenceEquals(fixture.State.Roster[0], selected[0]));
    Assert.True(CardText(SquadSlot(view, 1)).Contains("Alpha"));
    Assert.True(CardText(SquadSlot(view, 0)).Contains("empty"));
    Assert.Equal("Squad: 1/3", view.GetNode<Label>("%SquadCount").Text);

    Assert.True(SquadChoice(view, "Alpha").Disabled); // destination consumed again
    Assert.True(SquadChoice(view, "Bravo").Disabled);

    SquadSlot(view, 0).GetNode<Button>("%ChooseUnit")
      .EmitSignal(Button.SignalName.Pressed); // marking a destination re-enables choices
    Assert.True(SquadChoice(view, "Alpha").Disabled); // still occupies slot 1
    Assert.False(SquadChoice(view, "Bravo").Disabled);
    Assert.False(SquadChoice(view, "Charlie").Disabled);
  }

  [TestCase(TestName = "Replace and remove keep other slots and leave holes")]
  public async Task ReplaceAndRemoveKeepStableSlots()
  {
    await using var cleanup = new DeferredNodeCleanup();
    var (fixture, pending) = Mission(
      [MakeEntry("Alpha"), MakeEntry("Bravo"), MakeEntry("Charlie")]);
    using var _ = fixture;
    var view = PresentedSquadView(pending, fixture.State, fixture.Session);

    ChooseSquadUnit(view, 0, "Alpha");
    ChooseSquadUnit(view, 1, "Bravo");
    ChooseSquadUnit(view, 0, "Charlie"); // replace: displaced Alpha becomes available
    Press(SquadSlot(view, 1).GetNode<Button>("%RemoveUnit")); // clear: leaves a hole

    var selected = view.GetSelectedCombatants();
    Assert.Equal(1, selected.Count);
    Assert.True(ReferenceEquals(fixture.State.Roster[2], selected[0]));
    Assert.True(CardText(SquadSlot(view, 0)).Contains("Charlie"));
    Assert.True(CardText(SquadSlot(view, 1)).Contains("empty"));
    Assert.Equal("Squad: 1/3", view.GetNode<Label>("%SquadCount").Text);

    SquadSlot(view, 2).GetNode<Button>("%ChooseUnit")
      .EmitSignal(Button.SignalName.Pressed); // mark a destination to see availability
    Assert.True(SquadChoice(view, "Charlie").Disabled); // the occupied unit stays unavailable
    Assert.False(SquadChoice(view, "Alpha").Disabled); // cleared units become available again
    Assert.False(SquadChoice(view, "Bravo").Disabled);
  }

  [TestCase(TestName = "Occupied cards summarize equipment, mods, and effective stats")]
  public async Task OccupiedCardsSummarizeEquipmentAndStats()
  {
    await using var cleanup = new DeferredNodeCleanup();
    var (fixture, pending) = Mission([MakeEntry("Alpha", MakeCombatantData(
      "Alpha", health: 20, actionPoints: 8, movement: 14, vision: 22, aim: 60, modSlotCount: 1))]);
    using var _ = fixture;
    Combatant alpha = fixture.State.Roster[0];
    var rifle = new FirearmWeapon(MakeFirearmWeaponData("Rifle", modSlots: 1));
    rifle.GetModSlots()[0].Equip(new MultiStatMod
    { StatMods = [new AimStatMod { Modifiers = [StatModifier.Add(10)] }] });
    alpha.EquipWeapon(rifle);
    alpha.EquipArmor(MakeArmor("Vest"));
    alpha.EquipItem(new EquippableItem(MakeItemData("Medkit")), 0);
    alpha.GetModSlots()[0].Equip(new MultiStatMod { Name = "Nerves", StatMods = [] });

    var view = PresentedSquadView(pending, fixture.State, fixture.Session);
    ChooseSquadUnit(view, 0, "Alpha");

    string card = CardText(SquadSlot(view, 0));
    Assert.True(card.Contains("Rifle"), $"Weapon missing from '{card}'.");
    Assert.True(card.Contains("Vest"), $"Armor missing from '{card}'.");
    Assert.True(card.Contains("Medkit"), $"Utility item missing from '{card}'.");
    Assert.True(card.Contains("Aim: 70"), $"Weapon-contributed Aim missing from '{card}'.");
    Assert.True(card.Contains("Health: 20"), $"Health missing from '{card}'.");
    Assert.True(card.Contains("Movement: 14"), $"Movement missing from '{card}'.");
    Assert.True(card.Contains("Nerves"), $"Personal mod missing from '{card}'.");

    var selected = view.GetSelectedCombatants();
    Assert.True(ReferenceEquals(rifle, selected[0].EquippedWeapon.RequireSome()));
    Assert.Equal("Vest", selected[0].EquippedArmor.RequireSome().Item.ItemName);
  }

  [TestCase(TestName = "Unequipped slots have explicit empty equipment text")]
  public async Task UnequippedUnitsShowEmptyEquipmentText()
  {
    await using var cleanup = new DeferredNodeCleanup();
    var (fixture, pending) = Mission([MakeEntry("Alpha")]);
    using var _ = fixture;
    var view = PresentedSquadView(pending, fixture.State, fixture.Session);
    ChooseSquadUnit(view, 0, "Alpha");

    string card = CardText(SquadSlot(view, 0));
    Assert.True(card.Contains("Weapon:"), card);
    Assert.True(card.Contains("empty"), card);
    Assert.True(card.Contains("Armor:"), card);
  }

  [TestCase(TestName = "Editor round-trip keeps selection and refreshes summaries")]
  public async Task EditorRoundTripKeepsSelectionAndRefreshes()
  {
    await using var cleanup = new DeferredNodeCleanup();
    var (fixture, pending) = Mission(
      [MakeEntry("Alpha")],
      armory: [MakeFirearmWeaponData("Rifle"), MakeArmorData("Vest"), MakeItemData("Medkit")]);
    using var _ = fixture;
    var manager = CreatePresentedManager(fixture);

    var view = CreateSquadLoadoutView();
    view.Configure(pending.Event.Definition.Title, 3, allowEquipmentEditing: true,
      mission: pending.Event.Definition);
    manager.Push(view); // ViewChanged presents the configured view
    ChooseSquadUnit(view, 0, "Alpha");

    // Open the nested editor through the real button and drive its armory browser.
    Press(SquadSlot(view, 0).GetNode<Button>("%EditUnit"));
    var editor = (UnitView)manager.Current;
    Assert.False(view.IsVisibleInTree());

    editor.SelectSlot(UnitViewSlot.Weapon);
    Press(ArmoryButton(editor, "Rifle"));
    editor.SelectSlot(UnitViewSlot.Armor);
    Press(ArmoryButton(editor, "Vest"));
    editor.SelectSlot(UnitViewSlot.Utility(0));
    Press(ArmoryButton(editor, "Medkit"));

    Press(editor.GetNode<Button>("%BackButton"));

    Assert.True(ReferenceEquals(view, manager.Current));
    Assert.True(view.IsVisibleInTree());
    var selected = view.GetSelectedCombatants(); // selection survived the editor round-trip
    Assert.Equal(1, selected.Count);
    Assert.True(ReferenceEquals(fixture.State.Roster[0], selected[0]));
    Assert.Equal("Rifle", selected[0].EquippedWeapon.RequireSome().ItemName); // live equipment
    Assert.True(selected[0].EquippedArmor.RequireSome().Item.ItemName == "Vest");
    Assert.Equal("Medkit", selected[0].Inventory[0].ItemName);

    string card = CardText(SquadSlot(view, 0)); // summaries refreshed on return
    Assert.True(card.Contains("Rifle"), card);
    Assert.True(card.Contains("Vest"), card);
    Assert.True(card.Contains("Medkit"), card);
  }

  [TestCase(false, TestName = "Missing UnitViewScene export is an authoring error")]
  [TestCase(true, TestName = "A wrong-root UnitViewScene frees its instance and throws")]
  public async Task InvalidUnitViewScenePreservesSelection(bool wrongRoot)
  {
    await using var cleanup = new DeferredNodeCleanup();
    var (fixture, pending) = Mission([MakeEntry("Alpha")]);
    using var _ = fixture;
    var view = PresentedSquadView(pending, fixture.State, fixture.Session);
    view.UnitViewScene = wrongRoot ? Pack(new Label { Name = "NotAUnitView" }) : null;
    ChooseSquadUnit(view, 0, "Alpha");

    // Free (not QueueFree) leaves no orphan behind the synchronous throw. Direct call:
    // exceptions inside pressed-signal handlers are logged by Godot, not raised through
    // EmitSignal, so the authoring guard is invoked directly.
    long orphansBefore = (long)Performance.Singleton.GetMonitor(Performance.Monitor.ObjectOrphanNodeCount);
    Assert.Throws<InvalidOperationException>(() => view.EditUnit(0));
    Assert.Equal(orphansBefore,
      (long)Performance.Singleton.GetMonitor(Performance.Monitor.ObjectOrphanNodeCount));
    Assert.Equal(1, view.GetSelectedCombatants().Count); // selection untouched
  }

  [TestCase(false, TestName = "Missing SquadSlotCardScene export is an authoring error")]
  [TestCase(true, TestName = "A wrong-root SquadSlotCardScene frees its instance and throws")]
  public async Task InvalidSlotCardScenePreservesEmptySelection(bool wrongRoot)
  {
    await using var cleanup = new DeferredNodeCleanup();
    var (fixture, _) = Mission([MakeEntry("Alpha")]);
    using var __ = fixture;
    var view = AddToTree(CreateSquadLoadoutView());
    view.Configure("Operation Iron", 3, allowEquipmentEditing: true);
    view.SquadSlotCardScene = wrongRoot ? Pack(new Label { Name = "NotASquadSlotCard" }) : null;

    // Free (not QueueFree) leaves no orphan behind the synchronous throw. Direct call:
    // Present builds every card, and exceptions inside signal handlers are logged by
    // Godot, not raised through EmitSignal, so the authoring guard is invoked directly.
    long orphansBefore = (long)Performance.Singleton.GetMonitor(Performance.Monitor.ObjectOrphanNodeCount);
    Assert.Throws<InvalidOperationException>(() =>
      view.Present(fixture.State, fixture.Session));
    Assert.Equal(orphansBefore,
      (long)Performance.Singleton.GetMonitor(Performance.Monitor.ObjectOrphanNodeCount));
    Assert.Equal(0, view.GetSelectedCombatants().Count); // selection untouched
  }

  [TestCase(TestName = "Injured units are blocked from mission deployment")]
  public async Task InjuredUnitsAreBlockedFromMissionDeployment()
  {
    await using var cleanup = new DeferredNodeCleanup();
    var (fixture, pending) = Mission([MakeConditionEntry("Alpha")]);
    using var _ = fixture;
    Combatant alpha = fixture.State.Roster[0];
    fixture.ReturnFromMission((alpha, 25, 0)); // 25% damage: Injured (6d) plus Tired (1d)

    var view = PresentedSquadView(pending, fixture.State, fixture.Session);

    string choice = SquadChoice(view, "Alpha").Text;
    Assert.True(choice.Contains("Injured (6d)"), $"Expected the injury label with countdown, got '{choice}'.");
    Assert.True(choice.Contains("Tired (1d)"), $"Expected the fatigue label with countdown, got '{choice}'.");
    Assert.False(view.GetNode<Label>("%SelectionPrompt").Text.Contains("Exceptional"),
      "A normal mission shows no exceptional-deployment notice.");

    SquadSlot(view, 0).GetNode<Button>("%ChooseUnit").EmitSignal(Button.SignalName.Pressed);
    Assert.True(SquadChoice(view, "Alpha").Disabled);
    Assert.Equal(0, view.GetSelectedCombatants().Count);
  }

  [TestCase(TestName = "Exhausted units are blocked while Tired and Weary remain deployable")]
  public async Task ExhaustedIsBlockedWhileTiredAndWearyStayDeployable()
  {
    await using var cleanup = new DeferredNodeCleanup();
    var (fixture, pending) = Mission(
      [MakeConditionEntry("Alpha"), MakeConditionEntry("Bravo"), MakeConditionEntry("Charlie")]);
    using var _ = fixture;
    Combatant alpha = fixture.State.Roster[0];
    Combatant bravo = fixture.State.Roster[1];
    Combatant charlie = fixture.State.Roster[2];
    fixture.ReturnFromMission((alpha, 0, 0));
    fixture.ReturnFromMission((alpha, 0, 0));
    fixture.ReturnFromMission((alpha, 0, 0)); // Exhausted
    fixture.ReturnFromMission((bravo, 0, 0)); // Tired
    fixture.ReturnFromMission((charlie, 0, 0));
    fixture.ReturnFromMission((charlie, 0, 0)); // Weary

    var view = PresentedSquadView(pending, fixture.State, fixture.Session);
    SquadSlot(view, 0).GetNode<Button>("%ChooseUnit").EmitSignal(Button.SignalName.Pressed);

    Assert.True(SquadChoice(view, "Alpha").Disabled);
    Assert.False(SquadChoice(view, "Bravo").Disabled);
    Assert.False(SquadChoice(view, "Charlie").Disabled);

    SquadChoice(view, "Bravo").EmitSignal(BaseButton.SignalName.Pressed); // Tired stays deployable
    var selected = view.GetSelectedCombatants();
    Assert.Equal(1, selected.Count);
    Assert.True(ReferenceEquals(bravo, selected[0]));
  }

  [TestCase(TestName = "The unfit-deployment override enables blocked units once each")]
  public async Task OverrideEnablesUnfitUnitsOnceEach()
  {
    await using var cleanup = new DeferredNodeCleanup();
    var (fixture, pending) = Mission([MakeConditionEntry("Alpha"), MakeConditionEntry("Bravo")],
      mission: MakeEvent("Last Stand", GeoscapeEventKind.TacticalBattle, allowUnfitDeployment: true));
    using var _ = fixture;
    Combatant alpha = fixture.State.Roster[0];
    Combatant bravo = fixture.State.Roster[1];
    fixture.ReturnFromMission((alpha, 25, 0)); // Injured + Tired
    fixture.ReturnFromMission((bravo, 0, 0));
    fixture.ReturnFromMission((bravo, 0, 0));
    fixture.ReturnFromMission((bravo, 0, 0)); // Exhausted

    var view = PresentedSquadView(pending, fixture.State, fixture.Session);

    Assert.True(view.GetNode<Label>("%SelectionPrompt").Text.Contains("Exceptional deployment"));

    SquadSlot(view, 0).GetNode<Button>("%ChooseUnit").EmitSignal(Button.SignalName.Pressed);
    Assert.False(SquadChoice(view, "Alpha").Disabled);
    Assert.False(SquadChoice(view, "Bravo").Disabled);
    SquadChoice(view, "Alpha").EmitSignal(BaseButton.SignalName.Pressed);

    var selected = view.GetSelectedCombatants();
    Assert.Equal(1, selected.Count);
    Assert.True(ReferenceEquals(alpha, selected[0]));

    SquadSlot(view, 1).GetNode<Button>("%ChooseUnit").EmitSignal(Button.SignalName.Pressed);
    Assert.True(SquadChoice(view, "Alpha").Disabled); // duplicates stay blocked under the override
    Assert.False(SquadChoice(view, "Bravo").Disabled);
    var after = view.GetSelectedCombatants();
    Assert.Equal(1, after.Count);
    Assert.True(ReferenceEquals(alpha, after[0]));
  }

  [TestCase(TestName = "Selected override cards show countdowns and penalized stats")]
  public async Task OverrideCardsShowCountdownsAndPenalizedStats()
  {
    await using var cleanup = new DeferredNodeCleanup();
    var (fixture, pending) = Mission([MakeConditionEntry("Alpha")],
      mission: MakeEvent("Last Stand", GeoscapeEventKind.TacticalBattle, allowUnfitDeployment: true));
    using var _ = fixture;
    Combatant alpha = fixture.State.Roster[0];
    fixture.ReturnFromMission((alpha, 25, 0)); // Injured multipliers on top of Tired's

    var view = PresentedSquadView(pending, fixture.State, fixture.Session);
    ChooseSquadUnit(view, 0, "Alpha");

    string card = CardText(SquadSlot(view, 0));
    Assert.True(card.Contains("Injured (6d)"), $"Injury countdown missing from '{card}'.");
    Assert.True(card.Contains("Tired (1d)"), $"Fatigue countdown missing from '{card}'.");
    // Hand-derived: health 100x0.75, aim 60x0.90x0.90, movement 14x0.90, will 50x0.90.
    Assert.True(card.Contains("Health: 75"), $"Condition-reduced health missing from '{card}'.");
    Assert.True(card.Contains("Aim: 49"), $"Condition-reduced aim missing from '{card}'.");
    Assert.True(card.Contains("Movement: 13"), $"Condition-reduced movement missing from '{card}'.");
    Assert.True(card.Contains("Will: 45"), $"Condition-reduced will missing from '{card}'.");
  }

  [TestCase(TestName = "A positive recovery interval never rounds down to zero")]
  public async Task PositiveRecoveryIntervalsNeverRoundDownToZero()
  {
    await using var cleanup = new DeferredNodeCleanup();
    var fixture = new GeoscapeFixture(MakeStart(
      roster: [MakeConditionEntry("Alpha")],
      timeline: [MakeScheduled(1439, MakeEvent("Operation Iron", GeoscapeEventKind.TacticalBattle))]));
    using var _ = fixture;
    Combatant alpha = fixture.State.Roster[0];
    fixture.ReturnFromMission((alpha, 0, 0)); // Tired until tick 1440
    fixture.AdvanceTicks(1439); // the mission fires one tick before the fatigue deadline
    fixture.OpenResolution(fixture.ActiveEvent);
    var view = PresentedSquadView(
      fixture.Session.PendingResolution.RequireSome("Expected pending mission."),
      fixture.State, fixture.Session);

    string choice = SquadChoice(view, "Alpha").Text;
    Assert.True(choice.Contains("Tired (1d)"), $"Expected a one-day countdown, got '{choice}'.");
  }

  [TestCase(TestName = "The override retains selected exhausted units across editor navigation")]
  public async Task OverrideRetainsSelectedExhaustedUnitsAcrossNavigation()
  {
    await using var cleanup = new DeferredNodeCleanup();
    var (fixture, pending) = Mission([MakeConditionEntry("Alpha")],
      armory: [MakeFirearmWeaponData("Rifle")],
      mission: MakeEvent("Last Stand", GeoscapeEventKind.TacticalBattle, allowUnfitDeployment: true));
    using var _ = fixture;
    Combatant alpha = fixture.State.Roster[0];
    fixture.ReturnFromMission((alpha, 0, 0));
    fixture.ReturnFromMission((alpha, 0, 0));
    fixture.ReturnFromMission((alpha, 0, 0)); // Exhausted: eligible only through the override

    var manager = CreatePresentedManager(fixture);

    var view = CreateSquadLoadoutView();
    view.Configure(pending.Event.Definition.Title, 3, allowEquipmentEditing: true,
      mission: pending.Event.Definition);
    manager.Push(view); // ViewChanged presents the configured mission context
    ChooseSquadUnit(view, 0, "Alpha");

    Press(SquadSlot(view, 0).GetNode<Button>("%EditUnit"));
    var editor = (UnitView)manager.Current;
    editor.SelectSlot(UnitViewSlot.Weapon);
    Press(ArmoryButton(editor, "Rifle"));
    Press(editor.GetNode<Button>("%BackButton"));

    Assert.True(ReferenceEquals(view, manager.Current));
    var selected = view.GetSelectedCombatants(); // the covered re-present kept the selection
    Assert.Equal(1, selected.Count);
    Assert.True(ReferenceEquals(alpha, selected[0]));
    Assert.Equal("Rifle", selected[0].EquippedWeapon.RequireSome().ItemName);
    string card = CardText(SquadSlot(view, 0));
    Assert.True(card.Contains("Rifle"), card);
    Assert.True(card.Contains("Exhausted"), card);
  }

  [TestCase(TestName = "Generic interrogation selects injured units without any pending mission")]
  public async Task GenericInterrogationAllowsInjuredUnitsWithoutAPendingMission()
  {
    await using var cleanup = new DeferredNodeCleanup();
    var fixture = new GeoscapeFixture(MakeStart(roster: [MakeConditionEntry("Alpha")]));
    using var _ = fixture;
    Combatant alpha = fixture.State.Roster[0];
    fixture.ReturnFromMission((alpha, 25, 0)); // Injured + Tired

    var view = AddToTree(CreateSquadLoadoutView());
    view.Configure("Interrogation — Alpha (Cult)", 2, allowEquipmentEditing: false);
    view.Present(fixture.State, fixture.Session);

    Assert.False(fixture.Session.PendingResolution.IsSome); // no mission anywhere to infer from
    Assert.False(view.GetNode<Label>("%SelectionPrompt").Text.Contains("Exceptional"),
      "A generic configuration carries no exceptional-deployment notice.");
    string choice = SquadChoice(view, "Alpha").Text;
    Assert.True(choice.Contains("Injured (6d)"), $"Expected the condition label, got '{choice}'.");

    ChooseSquadUnit(view, 0, "Alpha"); // deployment rules do not restrict interrogation
    var selected = view.GetSelectedCombatants();
    Assert.Equal(1, selected.Count);
    Assert.True(ReferenceEquals(alpha, selected[0]));
    Assert.False(SquadSlot(view, 0).GetNode<Button>("%EditUnit").Visible); // editing stays disabled
  }

  [TestCase(TestName = "Reconfiguring to generic clears mission policy and picks despite a pending override")]
  public async Task ReconfiguringToGenericClearsMissionPolicyAndPicks()
  {
    await using var cleanup = new DeferredNodeCleanup();
    var (fixture, pending) = Mission([MakeConditionEntry("Alpha"), MakeConditionEntry("Bravo")],
      mission: MakeEvent("Last Stand", GeoscapeEventKind.TacticalBattle, allowUnfitDeployment: true));
    using var _ = fixture;
    Combatant alpha = fixture.State.Roster[0];
    fixture.ReturnFromMission((alpha, 25, 0)); // Injured + Tired: barred from the mission itself

    var view = AddToTree(CreateSquadLoadoutView());
    view.Configure(pending.Event.Definition.Title, 3, allowEquipmentEditing: true,
      mission: pending.Event.Definition);
    view.Present(fixture.State, fixture.Session);
    Assert.True(view.GetNode<Label>("%SelectionPrompt").Text.Contains("Exceptional deployment"));
    ChooseSquadUnit(view, 0, "Alpha"); // the override authorizes the unfit unit in mission prep
    ChooseSquadUnit(view, 1, "Bravo"); // two picks exist to prove the reset below

    view.Configure("Interrogation", 2, allowEquipmentEditing: false); // generic: context cleared
    view.Present(fixture.State, fixture.Session);

    Assert.True(fixture.Session.PendingResolution.IsSome); // the unrelated override is still pending
    Assert.Equal("Squad: 0/2", view.GetNode<Label>("%SquadCount").Text); // picks were reset
    Assert.False(view.GetNode<Label>("%SelectionPrompt").Text.Contains("Exceptional"));
    ChooseSquadUnit(view, 0, "Alpha"); // injured unit selectable for interrogation
    var selected = view.GetSelectedCombatants();
    Assert.Equal(1, selected.Count);
    Assert.True(ReferenceEquals(alpha, selected[0]));
  }
}
