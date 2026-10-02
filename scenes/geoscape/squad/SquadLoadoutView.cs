using CampaignGameState = global::FunProject.GameState.GameState;
using FunProject.Combatants;
using FunProject.Combatants.Conditions;
using FunProject.Strategic;
using Godot;
using System;
using FunProject.Scenes.Ext;
using LanguageExt.UnsafeValueAccess;

namespace FunProject.Geoscape;

// Shared squad selection screen behind openers that configure it: mission preparation
// (three editable slots, the mission's title, mission-scoped deployment eligibility) and
// interrogation preparation (two slots, equipment editing disabled, a captive-targets
// heading, unrestricted roster selection). Presentation only — the screen owns nothing but
// a temporary slot selection over the roster's live combatant references and re-presents
// from campaign truth on every activation, so equipment edits made in the nested UnitView
// show up on return without this view caching anything. Deployment gates apply only while
// an explicit mission context was configured; a generic configuration never infers one.
// Back discards the slot selection; it never mutates campaign state.
public sealed partial class SquadLoadoutView : GeoscapeView
{
  [Export] public PackedScene? UnitViewScene { get; set; }
  [Export] public PackedScene? SquadSlotCardScene { get; set; }

  // Bound by Configure (heading/policy) and Present (state/session); render paths assume
  // both ran.
  private CampaignGameState _state = null!;
  private GeoscapeSession _session = null!;
  private string _heading = null!;
  private bool _allowEquipmentEditing;
  private Option<GeoscapeEventDefinition> _mission;
  private int? _choosingSlot;
  private Option<Combatant>[] _slots = [];

  public void Configure(string heading, uint capacity, bool allowEquipmentEditing,
    Option<GeoscapeEventDefinition> mission = default)
  {
    _heading = heading;
    _allowEquipmentEditing = allowEquipmentEditing;
    _mission = mission;
    _choosingSlot = null;
    _slots = new Option<Combatant>[capacity];
  }

  // Every activation re-presents from campaign truth under the configured heading,
  // capacity, and mission gate.
  public override void Present(CampaignGameState state, GeoscapeSession session)
  {
    _state = state;
    _session = session;
    RebuildAll();
  }

  private void RebuildAll()
  {
    var title = GetNode<Label>("%Title");
    title.Text = _heading;
    title.TooltipText = _heading; // the authored label clips; the tooltip keeps long target lists readable
    GetNode<Label>("%SquadCount").Text =
      $"Squad: {_slots.AsValueEnumerable().Count(c => c.IsSome)}/{_slots.Length}";
    string prompt = _choosingSlot is { } slot
      ? $"Assigning Slot {slot + 1} — pick a unit below."
      : "Pick Choose on a slot, then select a unit.";
    if (_mission.Case is GeoscapeEventDefinition { AllowUnfitDeployment: true })
      prompt += "\nExceptional deployment authorized — injured or exhausted units may join this mission.";
    GetNode<Label>("%SelectionPrompt").Text = prompt;
    RebuildSlots();
    RebuildChoices();
  }

  // The preparation boundary output: the original campaign combatants in slot order.
  // Equipment is read live through those references; nothing is cloned here.
  public SysColGeneric.List<Combatant> GetSelectedCombatants()
    => _slots.AsValueEnumerable()
      .Where(c => c.IsSome).Select(v => v.ValueUnsafe()!).ToList();

  // Mission-aware eligibility, only while an explicit mission context was configured: the
  // session's own gate against that mission. Generic configurations permit any roster
  // unit; deployment rules belong to mission preparation, not to interrogation or any
  // other shared use.
  private bool CanDeploy(Combatant unit)
    => _mission.Case is not GeoscapeEventDefinition mission || _session.CanDeploy(unit, mission);

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
        Text = "No units available for this mission.",
        AutowrapMode = TextServer.AutowrapMode.WordSmart,
      });
      return;
    }

    foreach (Combatant unit in _state.Roster)
    {
      // Inert until a destination slot is marked (Choose), while the unit occupies one, and
      // — under an explicit mission context — while the campaign bars the unit from that
      // mission (injury or exhaustion without an exceptional-deployment override).
      var button = new Button
      {
        Text = RosterButtonText(unit),
        TooltipText = RosterButtonText(unit),
        CustomMinimumSize = new Vector2(0, 58),
        Alignment = HorizontalAlignment.Left,
        AutowrapMode = TextServer.AutowrapMode.WordSmart,
        ClipText = true,
        Disabled = _choosingSlot is null || InSquad(unit) || !CanDeploy(unit),
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
      instance.Free(); // free now: rejected roots must not linger to frame end
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

  // Both records read separately from the bound campaign's registry; names resolve
  // through the same system so shared campaign rules label every unit consistently. Only
  // active records render — a healthy/fresh unit contributes no part.
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
