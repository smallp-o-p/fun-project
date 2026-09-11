using CampaignGameState = global::FunProject.GameState.GameState;
using FunProject.Combatants;
using FunProject.Items;
using FunProject.Stats;
using FunProject.Strategic;
using Godot;
using System;
using System.Linq;
using LanguageExt.UnsafeValueAccess;

namespace FunProject.Geoscape;

// Pre-mission squad preparation: three fixed slots filled from the campaign roster, with
// each occupied unit's current equipment and effective stats. Presentation only — the
// screen owns nothing but a temporary SquadSelection over the roster's live combatant
// references and re-presents from campaign truth on every activation, so equipment edits
// made in the nested UnitView show up on return without this view caching anything.
// Back discards the squad selection; it never mutates campaign state.
public sealed partial class SquadLoadoutView : GeoscapeView
{
  [Export] public PackedScene? UnitViewScene { get; set; }
  [Export] public PackedScene? SquadSlotCardScene { get; set; }

  private const uint Capacity = 3U;

  private CampaignGameState? _state;
  private PendingResolution? _mission;
  private uint? _choosingSlot;
  private readonly Option<Combatant>[] _slots = new Option<Combatant>[Capacity];

  // The mission dialog stays open while this view lives, so every activation reads the
  // pending mission straight from the session and retains it for the title.
  public override void Present(CampaignGameState state, GeoscapeSession session)
  {
    _state = state;
    _mission = session.PendingResolution.RequireSome(
      "SquadLoadoutView requires a pending mission; it is only reachable while a mission dialog is open.");
    RebuildAll();
  }

  private void RebuildAll()
  {
    if (_state is null)
      return;

    GetNode<Label>("%Title").Text = _mission?.Event.Definition.Title ?? "";
    GetNode<Label>("%SquadCount").Text =
      $"Squad: {_slots.Count(c => c.IsSome)}/{Capacity}";
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
    ClearChildren(slots);
    for (uint slot = 0; slot < Capacity; slot++)
      BuildCard(slots, slot);
  }

  private void BuildCard(VBoxContainer slots, uint slot)
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
      unit is not null ? EquipmentText(unit) : "",
      unit is not null ? StatsText(unit) : "");
  }

  private void RebuildChoices()
  {
    var choices = GetNode<VBoxContainer>("%RosterChoices");
    ClearChildren(choices);
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
    return _slots.Any(opt => opt.Exists(c => ReferenceEquals(c, unit)));
  }

  private void BeginChoose(uint slot)
  {
    _choosingSlot = slot;
    RebuildAll(); // the prompt must name the destination before any unit is picked
  }

  private void RemoveUnit(uint slot)
  {
    _slots[slot] = None;
    RebuildAll();
  }

  private void ChooseUnit(Combatant unit)
  {
    if (_choosingSlot is null)
      return;

    _slots[(int)_choosingSlot] = unit;
    _choosingSlot = null;

    RebuildAll();
  }

  public void EditUnit(uint slot)
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

  private static string EquipmentText(Combatant unit)
  {
    SysColGeneric.List<string> lines =
    [
      unit.EquippedWeapon.Match(
        weapon => $"Weapon: {weapon.ItemName}", () => "Weapon: — empty —"),
      unit.EquippedArmor.Match(
        armor => $"Armor: {armor.Item.ItemName}", () => "Armor: — empty —"),
    ];

    for (int i = 0; i < unit.MaxInventorySize; i++)
      lines.Add(unit.Inventory.TryGetValue(i, out EquippableItem? item)
        ? $"Utility {i + 1}: {item.ItemName}"
        : $"Utility {i + 1}: — empty —");

    lines.Add(ModsText("Personal mods", unit.GetModSlots()));
    unit.EquippedWeapon.IfSome(weapon => lines.Add(ModsText("Weapon mods", weapon.GetModSlots())));

    return string.Join("\n", lines);
  }

  private static string ModsText(string label, Godot.Collections.Array<ModSlot> slots)
  {
    SysColGeneric.List<string> names = [];
    foreach (ModSlot slot in slots)
      slot.EquippedMod.IfSome(mod => names.Add(mod.Name));
    return names.Count > 0 ? $"{label}: {string.Join(", ", names)}" : $"{label}: — empty —";
  }

  private static string StatsText(Combatant unit) => string.Join("  ",
    StatText<HealthStat>(unit, "Health"),
    StatText<ActionPointsStat>(unit, "Action Points"),
    StatText<WillStat>(unit, "Will"),
    StatText<MovementStat>(unit, "Movement"),
    StatText<VisionStat>(unit, "Vision"),
    StatText<AimStat>(unit, "Aim"));

  // Same contribution set as UnitView: the combatant's own mods/buffs plus the equipped
  // weapon's, so a weapon's stat mods change the presented effective stats.
  private static string StatText<TStat>(Combatant unit, string label) where TStat : Stat
  {
    var contributions = unit.StatContributions().AsValueEnumerable()
      .Concat(unit.EquippedWeapon.Match<SysColGeneric.IEnumerable<StatMod>>(
       weapon => weapon.StatContributions,
       []))
      .ToArray();

    return $"{label}: {Mathf.RoundToInt(unit.Resolve<TStat>(contributions))}";
  }

  private static void ClearChildren(Container parent)
  {
    foreach (Node child in parent.GetChildren())
    {
      parent.RemoveChild(child); // detach now: replaced rows must not linger to frame end
      child.QueueFree();
    }
  }
}
