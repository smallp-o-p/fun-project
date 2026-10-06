using CampaignGameState = global::FunProject.GameState.GameState;
using FunProject.Combatants;
using FunProject.Combatants.Conditions;
using FunProject.Strategic;
using Godot;
using System;
using System.Collections.Generic;
using FunProject.Scenes.Ext;
using LanguageExt.UnsafeValueAccess;

namespace FunProject.Geoscape;

// Selects live roster combatants in configured slots and refreshes their equipment and
// condition summaries when presented.
public partial class SquadLoadoutView : GeoscapeView
{
  [Export] public PackedScene? UnitViewScene { get; set; }
  [Export] public PackedScene? SquadSlotCardScene { get; set; }

  private CampaignGameState _state = null!;
  private GeoscapeSession _session = null!;
  private string _heading = null!;
  private bool _allowEquipmentEditing;
  private int? _choosingSlot;
  private Option<Combatant>[] _slots = [];

  public void Configure(string heading, uint capacity, bool allowEquipmentEditing)
  {
    _heading = heading;
    _allowEquipmentEditing = allowEquipmentEditing;
    _choosingSlot = null;
    _slots = new Option<Combatant>[capacity];
  }

  public override void Present(CampaignGameState state, GeoscapeSession session)
  {
    _state = state;
    _session = session;
    RebuildAll();
  }

  protected virtual void RebuildAll()
  {
    var title = GetNode<Label>("%Title");
    title.Text = _heading;
    title.TooltipText = _heading; // the authored label clips; the tooltip keeps long target lists readable
    GetNode<Label>("%SquadCount").Text =
      $"Squad: {_slots.AsValueEnumerable().Count(c => c.IsSome)}/{_slots.Length}";
    string prompt = _choosingSlot is { } slot
      ? $"Assigning Slot {slot + 1} — pick a unit below."
      : "Pick Choose on a slot, then select a unit.";
    GetNode<Label>("%SelectionPrompt").Text = prompt;

    RebuildSlots();
    RebuildChoices();
  }

  public SysColGeneric.List<Combatant> GetSelectedCombatants()
    => _slots.AsValueEnumerable()
      .Where(c => c.IsSome).Select(v => v.ValueUnsafe()!).ToList();

  protected virtual bool CanChoose(Combatant unit) => true;

  private void RebuildSlots()
  {
    var slots = GetNode<BoxContainer>("%Slots");
    slots.QueueFreeAllChildren();
    for (int slot = 0; slot < _slots.Length; slot++)
      BuildCard(slots, slot);
  }

  private void BuildCard(BoxContainer slots, int slot)
  {
    var scene = SquadSlotCardScene ?? throw new InvalidOperationException(
      "SquadLoadoutView requires SquadSlotCardScene; assign a PackedScene in the inspector.");
    Node instance = scene.Instantiate();
    if (instance is not SquadSlotCard card)
    {
      instance.Free();
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
      unit is not null ? OccupiedStatsText(unit) : "",
      allowEquipmentEditing: _allowEquipmentEditing);
  }

  private void RebuildChoices()
  {
    var choices = GetNode<VBoxContainer>("%RosterChoices");
    choices.QueueFreeAllChildren();

    if (_state.Roster.Count == 0)
    {
      choices.AddChild(new Label
      {
        Text = "No units available.",
        AutowrapMode = TextServer.AutowrapMode.WordSmart,
      });
      return;
    }

    foreach (Combatant unit in _state.Roster)
    {
      string label = RosterButtonText(unit);
      var button = new Button
      {
        Text = label,
        TooltipText = label,
        CustomMinimumSize = new Vector2(0, 58),
        Alignment = HorizontalAlignment.Left,
        AutowrapMode = TextServer.AutowrapMode.WordSmart,
        ClipText = true,
        Disabled = _choosingSlot is null || InSquad(unit) || !CanChoose(unit),
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

  // Handles an enabled choice: assign to the selected slot, clear the choosing state,
  // rebuild. RebuildChoices controls which choices are enabled.
  private void ChooseUnit(Combatant unit)
  {
    _slots[_choosingSlot!.Value] = unit;
    _choosingSlot = null;

    RebuildAll();
  }

  // The card-offered edit action; SquadSlotCard.Bind shows the Edit button only while
  // Configure enabled equipment editing.
  public void EditUnit(int slot)
  {
    _slots[slot].IfSome(OpenEditor);
  }

  private void OpenEditor(Combatant unit)
  {
    PackedScene scene = UnitViewScene ?? throw new InvalidOperationException(
      "SquadLoadoutView requires UnitViewScene; assign a PackedScene in the inspector.");
    Node instance = scene.Instantiate();
    if (instance is not UnitView view)
    {
      instance.Free();
      throw new InvalidOperationException(
        "SquadLoadoutView requires a UnitView scene root.");
    }
    view.BindUnit(unit); // retain the slot's unit before the view enters the tree
    RequestView(view);
  }

  // Conditions first (each label with the time until its next improvement) from the bound
  // campaign's registry, then the shared effective-stats summary through the campaign's
  // contribution composition (own + condition tiers + equipped weapon).
  private string OccupiedStatsText(Combatant unit)
  {
    string conditions = ConditionsText(unit, _session.Tick);
    string stats = CombatantSummary.StatsText(unit, "Health", _state.CampaignStatContributions(unit));
    return conditions.Length == 0 ? stats : $"Conditions: {conditions}\n{stats}";
  }

  // The roster button carries the same condition summary so blocked and merely penalized
  // units are readable at a glance; healthy units keep their bare name.
  private string RosterButtonText(Combatant unit)
  {
    string conditions = ConditionsText(unit, _session.Tick);
    return conditions.Length == 0 ? unit.Name : $"{unit.Name} — {conditions}";
  }

  private string ConditionsText(Combatant unit, long tick)
  {
    CombatantConditionSystem system = _state.Conditions;
    SysColGeneric.List<string> parts = [];
    if (system.GetInjury(unit).Case is InjuryState injury)
      parts.Add(TierText(system.InjuryName(injury.Tier), injury.RecoveryTick, tick));
    if (system.GetFatigue(unit).Case is FatigueState fatigue)
      parts.Add(TierText(system.FatigueName(fatigue.Tier), fatigue.RecoveryTick, tick));
    return string.Join(", ", parts);
  }

  private static string TierText(string name, long recoveryTick, long tick)
    => $"{name} ({FormatRemaining(recoveryTick - tick)})";

  // Remaining campaign ticks until the tier's next improvement, shown as whole days
  // rounded up so a positive interval never displays as zero.
  private static string FormatRemaining(long ticksRemaining) =>
    $"{CeilDiv(Math.Max(0, ticksRemaining), CampaignGameState.TicksFromDays(1))}d";

  private static long CeilDiv(long value, long divisor) => (value + divisor - 1) / divisor;
}
