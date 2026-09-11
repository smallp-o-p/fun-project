#nullable disable warnings
using System;
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
  // behind the pending resolution.
  private static (GeoscapeFixture Fixture, PendingResolution Pending) Mission(
    RosterEntryData[] roster,
    EquippableItemData[] armory = null,
    EquippableMod[] modStock = null)
  {
    var fixture = new GeoscapeFixture(MakeStart(
      roster: roster,
      timeline: [MakeScheduled(1, MakeEvent("Operation Iron", GeoscapeEventKind.TacticalBattle))],
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
    view.Present(state, session); // Present pulls the pending mission from the session
    return view;
  }

  private static Button ChoiceButton(SquadLoadoutView view, string unitName)
    => view.GetNode<VBoxContainer>("%RosterChoices").GetChildren()
      .AsValueEnumerable().OfType<Button>()
      .Single(button => button.Text.Contains(unitName));

  // Sequential by necessity: pressing Choose rebuilds the roster controls, so the choice
  // button must be reacquired AFTER that press, not captured beside it as an argument.
  private static void ChooseIntoSlot(SquadLoadoutView view, int slot, string unitName)
  {
    SlotCard(view, slot).GetNode<Button>("%ChooseUnit")
      .EmitSignal(Button.SignalName.Pressed);
    ChoiceButton(view, unitName).EmitSignal(Button.SignalName.Pressed);
  }

  private static void Press(params BaseButton[] buttons)
  {
    foreach (BaseButton button in buttons)
      button.EmitSignal(BaseButton.SignalName.Pressed);
  }

  private static Control SlotCard(SquadLoadoutView view, int slot)
    => view.GetNode<VBoxContainer>("%Slots").GetChild<Control>(slot);

  private static string CardText(Control card)
    => $"{card.GetNode<Label>("%CardTitle").Text}\n"
      + $"{card.GetNode<RichTextLabel>("%Equipment").Text}\n"
      + $"{card.GetNode<RichTextLabel>("%Stats").Text}";

  [TestCase(TestName = "An empty-roster mission presents three empty slots and no choices")]
  public async Task EmptyRosterPresentsThreeEmptySlots()
  {
    await using var cleanup = new DeferredNodeCleanup();
    var (fixture, pending) = Mission([]);
    using var _ = fixture;

    var view = PresentedSquadView(pending, fixture.State, fixture.Session);

    Assert.Equal("Operation Iron", view.GetNode<Label>("%Title").Text);
    Assert.Equal("Squad: 0/3", view.GetNode<Label>("%SquadCount").Text);
    Assert.Equal(3, view.GetNode<VBoxContainer>("%Slots").GetChildCount());
    Assert.Equal(0, view.GetSelectedCombatants().Count);

    var choiceText = view.GetNode<VBoxContainer>("%RosterChoices")
      .GetChild(0) is Label note ? note.Text : "";
    Assert.True(choiceText.Contains("No units"), $"Expected an empty-roster note, got '{choiceText}'.");
    foreach (Node card in view.GetNode<VBoxContainer>("%Slots").GetChildren())
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

  [TestCase(TestName = "Choices are inert without a destination and occupied units stay unavailable")]
  public async Task ChoicesRequireADestinationAndExcludeOccupiedUnits()
  {
    await using var cleanup = new DeferredNodeCleanup();
    var (fixture, pending) = Mission(
      [MakeEntry("Alpha"), MakeEntry("Bravo"), MakeEntry("Charlie")]);
    using var _ = fixture;
    var view = PresentedSquadView(pending, fixture.State, fixture.Session);

    Assert.True(ChoiceButton(view, "Alpha").Disabled); // no destination yet
    Assert.True(ChoiceButton(view, "Bravo").Disabled);

    ChooseIntoSlot(view, 1, "Alpha");

    var selected = view.GetSelectedCombatants();
    Assert.Equal(1, selected.Count);
    Assert.True(ReferenceEquals(fixture.State.Roster[0], selected[0]));
    Assert.True(CardText(SlotCard(view, 1)).Contains("Alpha"));
    Assert.True(CardText(SlotCard(view, 0)).Contains("empty"));
    Assert.Equal("Squad: 1/3", view.GetNode<Label>("%SquadCount").Text);

    Assert.True(ChoiceButton(view, "Alpha").Disabled); // destination consumed again
    Assert.True(ChoiceButton(view, "Bravo").Disabled);

    SlotCard(view, 0).GetNode<Button>("%ChooseUnit")
      .EmitSignal(Button.SignalName.Pressed); // marking a destination re-enables choices
    Assert.True(ChoiceButton(view, "Alpha").Disabled); // still occupies slot 1
    Assert.False(ChoiceButton(view, "Bravo").Disabled);
    Assert.False(ChoiceButton(view, "Charlie").Disabled);
  }

  [TestCase(TestName = "Replace and remove keep other slots and leave holes")]
  public async Task ReplaceAndRemoveKeepStableSlots()
  {
    await using var cleanup = new DeferredNodeCleanup();
    var (fixture, pending) = Mission(
      [MakeEntry("Alpha"), MakeEntry("Bravo"), MakeEntry("Charlie")]);
    using var _ = fixture;
    var view = PresentedSquadView(pending, fixture.State, fixture.Session);

    ChooseIntoSlot(view, 0, "Alpha");
    ChooseIntoSlot(view, 1, "Bravo");
    ChooseIntoSlot(view, 0, "Charlie"); // replace: displaced Alpha becomes available
    Press(SlotCard(view, 1).GetNode<Button>("%RemoveUnit")); // clear: leaves a hole

    var selected = view.GetSelectedCombatants();
    Assert.Equal(1, selected.Count);
    Assert.True(ReferenceEquals(fixture.State.Roster[2], selected[0]));
    Assert.True(CardText(SlotCard(view, 0)).Contains("Charlie"));
    Assert.True(CardText(SlotCard(view, 1)).Contains("empty"));
    Assert.Equal("Squad: 1/3", view.GetNode<Label>("%SquadCount").Text);

    SlotCard(view, 2).GetNode<Button>("%ChooseUnit")
      .EmitSignal(Button.SignalName.Pressed); // mark a destination to see availability
    Assert.True(ChoiceButton(view, "Charlie").Disabled); // the occupied unit stays unavailable
    Assert.False(ChoiceButton(view, "Alpha").Disabled); // cleared units become available again
    Assert.False(ChoiceButton(view, "Bravo").Disabled);
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
    ChooseIntoSlot(view, 0, "Alpha");

    string card = CardText(SlotCard(view, 0));
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
    ChooseIntoSlot(view, 0, "Alpha");

    string card = CardText(SlotCard(view, 0));
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
    var manager = new GeoscapeViewManager { Name = "Manager" };
    var root = CreateBaseView();
    manager.RootView = root;
    manager.AddChild(root);
    AddToTree(manager);
    manager.ViewChanged += view => view.Present(fixture.State, fixture.Session);

    var view = CreateSquadLoadoutView();
    manager.Push(view); // ViewChanged presents; Present sources the mission from the session
    ChooseIntoSlot(view, 0, "Alpha");

    // Open the nested editor through the real button and drive its armory browser.
    Press(SlotCard(view, 0).GetNode<Button>("%EditUnit"));
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

    string card = CardText(SlotCard(view, 0)); // summaries refreshed on return
    Assert.True(card.Contains("Rifle"), card);
    Assert.True(card.Contains("Vest"), card);
    Assert.True(card.Contains("Medkit"), card);
  }

  [TestCase(TestName = "Missing UnitViewScene export is an authoring error")]
  public async Task MissingUnitViewSceneThrows()
  {
    await using var cleanup = new DeferredNodeCleanup();
    var (fixture, pending) = Mission([MakeEntry("Alpha")]);
    using var _ = fixture;
    var view = PresentedSquadView(pending, fixture.State, fixture.Session);
    view.UnitViewScene = null;
    ChooseIntoSlot(view, 0, "Alpha");

    // Direct call: exceptions inside pressed-signal handlers are logged by Godot, not
    // raised through EmitSignal, so the authoring guard is invoked directly.
    Assert.Throws<InvalidOperationException>(() => view.EditUnit(0));
    Assert.Equal(1, view.GetSelectedCombatants().Count); // selection untouched
  }

  [TestCase(TestName = "A wrong-root UnitViewScene frees its instance and throws")]
  public async Task WrongRootUnitViewSceneThrows()
  {
    await using var cleanup = new DeferredNodeCleanup();
    var (fixture, pending) = Mission([MakeEntry("Alpha")]);
    using var _ = fixture;
    var view = PresentedSquadView(pending, fixture.State, fixture.Session);
    var probe = new Label { Name = "NotAUnitView" };
    var packed = new PackedScene();
    Error error = packed.Pack(probe);
    probe.Free();
    if (error != Error.Ok)
      throw new InvalidOperationException($"Test scene packing failed: {error}");
    view.UnitViewScene = packed;
    ChooseIntoSlot(view, 0, "Alpha");

    // Free (not QueueFree) leaves no orphan behind the synchronous throw.
    long orphansBefore = (long)Performance.Singleton.GetMonitor(Performance.Monitor.ObjectOrphanNodeCount);
    Assert.Throws<InvalidOperationException>(() => view.EditUnit(0));
    Assert.Equal(orphansBefore,
      (long)Performance.Singleton.GetMonitor(Performance.Monitor.ObjectOrphanNodeCount));
  }

  [TestCase(TestName = "Missing SquadSlotCardScene export is an authoring error")]
  public async Task MissingSquadSlotCardSceneThrows()
  {
    await using var cleanup = new DeferredNodeCleanup();
    var (fixture, _) = Mission([MakeEntry("Alpha")]);
    using var __ = fixture;
    var view = AddToTree(CreateSquadLoadoutView());
    view.SquadSlotCardScene = null;

    // Direct call: Present builds every card, and exceptions inside signal handlers are
    // logged by Godot, not raised through EmitSignal, so the authoring guard is invoked
    // directly.
    Assert.Throws<InvalidOperationException>(() =>
      view.Present(fixture.State, fixture.Session));
    Assert.Equal(0, view.GetSelectedCombatants().Count); // selection untouched
  }

  [TestCase(TestName = "A wrong-root SquadSlotCardScene frees its instance and throws")]
  public async Task WrongRootSquadSlotCardSceneThrows()
  {
    await using var cleanup = new DeferredNodeCleanup();
    var (fixture, _) = Mission([MakeEntry("Alpha")]);
    using var __ = fixture;
    var view = AddToTree(CreateSquadLoadoutView());
    var probe = new Label { Name = "NotASquadSlotCard" };
    var packed = new PackedScene();
    Error error = packed.Pack(probe);
    probe.Free();
    if (error != Error.Ok)
      throw new InvalidOperationException($"Test scene packing failed: {error}");
    view.SquadSlotCardScene = packed;

    // Free (not QueueFree) leaves no orphan behind the synchronous throw.
    long orphansBefore = (long)Performance.Singleton.GetMonitor(Performance.Monitor.ObjectOrphanNodeCount);
    Assert.Throws<InvalidOperationException>(() =>
      view.Present(fixture.State, fixture.Session));
    Assert.Equal(orphansBefore,
      (long)Performance.Singleton.GetMonitor(Performance.Monitor.ObjectOrphanNodeCount));
    Assert.Equal(0, view.GetSelectedCombatants().Count); // selection untouched
  }

  private static Button ArmoryButton(UnitView editor, string itemName)
    => editor.GetNode<VBoxContainer>("%ArmoryList").GetChildren()
      .AsValueEnumerable().OfType<Button>()
      .Single(button => button.Text.Contains(itemName));
}
