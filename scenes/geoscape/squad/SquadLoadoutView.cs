using CampaignGameState = global::FunProject.GameState.GameState;
using FunProject.Combatants;
using FunProject.Strategic;
using Godot;
using System;
using FunProject.Scenes.Ext;
using LanguageExt.UnsafeValueAccess;

namespace FunProject.Geoscape;

// Shared squad selection screen behind openers that configure it: mission preparation
// (three editable slots, the mission's title) and interrogation preparation (two slots,
// equipment editing disabled, a captive-targets heading). Presentation only — the screen
// owns nothing but a temporary slot selection over the roster's live combatant references
// and re-presents from campaign truth on every activation, so equipment edits made in the
// nested UnitView show up on return without this view caching anything. Back discards the
// slot selection; it never mutates campaign state.
public sealed partial class SquadLoadoutView : GeoscapeView
{
  [Export] public PackedScene? UnitViewScene { get; set; }
  [Export] public PackedScene? SquadSlotCardScene { get; set; }

  private CampaignGameState? _state;
  private string? _heading;
  private bool _allowEquipmentEditing;
  private int? _choosingSlot;
  private Option<Combatant>[] _slots = [];

  // The opener's required configuration door (mission preparation: three editable slots
  // under the mission title; interrogation: two slots, editing disabled). It resets the
  // temporary slot selection so a reconfigured view never carries stale picks into a
  // different destination.
  public void Configure(string heading, uint capacity, bool allowEquipmentEditing)
  {
    ArgumentNullException.ThrowIfNull(heading);
    if (capacity == 0U)
      throw new ArgumentOutOfRangeException(nameof(capacity), capacity,
        "SquadLoadoutView capacity must be positive.");
    // Allocate the replacement slots before touching config so allocation failure cannot
    // partially reconfigure the view.
    var slots = new Option<Combatant>[capacity];
    _heading = heading;
    _allowEquipmentEditing = allowEquipmentEditing;
    _choosingSlot = null;
    _slots = slots;
  }

  // Openers must Configure before Present; presenting unconfigured is a caller bug.
  public override void Present(CampaignGameState state, GeoscapeSession session)
  {
    if (_heading is null)
      throw new InvalidOperationException(
        "SquadLoadoutView requires Configure(heading, capacity, allowEquipmentEditing) before Present.");
    _state = state;
    RebuildAll();
  }

  private void RebuildAll()
  {
    if (_state is null)
      return;

    var title = GetNode<Label>("%Title");
    title.Text = _heading;
    title.TooltipText = _heading; // the authored label clips; the tooltip keeps long target lists readable
    GetNode<Label>("%SquadCount").Text =
      $"Squad: {_slots.AsValueEnumerable().Count(c => c.IsSome)}/{_slots.Length}";
    GetNode<Label>("%SelectionPrompt").Text = _choosingSlot is { } slot
      ? $"Assigning Slot {slot + 1} — pick a unit below."
      : "Pick Choose on a slot, then select a unit.";
    RebuildSlots();
    RebuildChoices();
  }

  // The preparation boundary output: the original campaign combatants in slot order.
  // Equipment is read live through those references; nothing is cloned here.
  public SysColGeneric.List<Combatant> GetSelectedCombatants() =>
    _slots.AsValueEnumerable().Where(c => c.IsSome).Select(v => v.ValueUnsafe()!).ToList();

  private void RebuildSlots()
  {
    var slots = GetNode<VBoxContainer>("%Slots");
    slots.QueueFreeAllChildren();
    for (int slot = 0; slot < _slots.Length; slot++)
      BuildCard(slots, slot);
  }

  private void BuildCard(VBoxContainer slots, int slot)
  {
    var scene = SquadSlotCardScene ?? throw new InvalidOperationException(
      "SquadLoadoutView requires SquadSlotCardScene; assign a PackedScene in the inspector.");
    Node instance = scene.Instantiate();
    if (instance is not SquadSlotCard card)
    {
      instance.Free(); // free now: rejected roots must not linger to frame end
      throw new InvalidOperationException(
        "SquadLoadoutView requires SquadSlotCardScene whose root is a SquadSlotCard.");
    }

    var occupant = _slots[slot];
    Combatant? unit = occupant.Match<Combatant?>(u => u, () => null);

    slots.AddChild(card);
    card.EditRequested += () => EditUnit(slot);
    card.ChooseRequested += () => BeginChoose(slot);
    card.RemoveRequested += () => RemoveUnit(slot);

    card.Bind(
      unit is not null ? $"Slot {slot + 1} — {unit.Name}" : $"Slot {slot + 1} — empty",
      unit is not null,
      unit is not null ? CombatantSummary.EquipmentText(unit, includeMods: true) : "",
      unit is not null ? CombatantSummary.StatsText(unit, "Health") : "",
      allowEquipmentEditing: _allowEquipmentEditing);
  }

  private void RebuildChoices()
  {
    var choices = GetNode<VBoxContainer>("%RosterChoices");
    choices.QueueFreeAllChildren();

    if (_state!.Roster.Count == 0)
    {
      choices.AddChild(new Label { Text = "No units available for this mission." });
      return;
    }

    foreach (Combatant unit in _state.Roster)
    {
      // Inert until a destination slot is marked (Choose) and while the unit occupies one.
      var button = new Button
      {
        Text = unit.Name,
        Disabled = _choosingSlot is null || InSquad(unit),
      };
      button.Pressed += () => ChooseUnit(unit);
      choices.AddChild(button);
    }
  }

  private bool InSquad(Combatant unit)
  {
    return _slots.AsValueEnumerable().Any(opt => opt.Exists(c => ReferenceEquals(c, unit)));
  }

  private void BeginChoose(int slot)
  {
    _choosingSlot = slot;
    RebuildAll(); // the prompt must name the destination before any unit is picked
  }

  private void RemoveUnit(int slot)
  {
    _slots[slot] = None;
    RebuildAll();
  }

  private void ChooseUnit(Combatant unit)
  {
    if (_choosingSlot is null)
      return;
    if (InSquad(unit))
      return; // already picked (including the destination's current occupant): inert, the chooser stays open

    _slots[_choosingSlot.Value] = unit;
    _choosingSlot = null;

    RebuildAll();
  }

  public void EditUnit(int slot)
  {
    if (!_allowEquipmentEditing)
      throw new InvalidOperationException(
        "SquadLoadoutView.EditUnit is unavailable: Configure disabled equipment editing for this squad.");
    _slots[slot].IfSome(OpenEditor);
  }

  private void OpenEditor(Combatant unit)
  {
    PackedScene scene = UnitViewScene ?? throw new InvalidOperationException(
      "SquadLoadoutView requires UnitViewScene; assign a PackedScene in the inspector.");
    Node instance = scene.Instantiate();
    if (instance is not UnitView view)
    {
      instance.Free(); // free now: rejected roots must not linger to frame end
      throw new InvalidOperationException(
        "SquadLoadoutView requires a UnitView scene root.");
    }
    view.BindUnit(unit); // retain the slot's unit before the view enters the tree
    RequestView(view);
  }
}
