using CampaignGameState = global::FunProject.GameState.GameState;
using FunProject.Combatants;
using FunProject.Strategic;
using Godot;
using FunProject.Scenes.Ext;

namespace FunProject.Geoscape;

// Captivity screen: read-only inspection of the campaign's captured enemies and
// interrogation preparation. Captives are read live from state.Captivity (identity =
// combatant reference; rows distinguish identities even when names collide) and the view
// never mutates captivity, factions, the roster, or the Armory. The multi-captive
// selection is presentation state; Prepare interrogation opens the shared SquadLoadoutView
// configured for two distinct roster units with equipment editing disabled — the tactical
// minigame behind it is deferred, so nothing launches or resolves here. Back from that
// view returns to this one with the captive selection intact.
public sealed partial class CaptivityView : GeoscapeView
{
  [Export] public PackedScene? SquadViewScene { get; set; }

  private const uint InterrogationCapacity = 2U;

  private CampaignGameState? _state;
  private readonly SysColGeneric.List<Combatant> _selected = [];
  private Combatant? _details;

  public override void _Ready()
  {
    base._Ready();
    GetNode<Button>("%PrepareButton").Pressed += PrepareInterrogation;
  }

  // Refreshes from captivity truth: identity-based selections that are still captive are
  // retained, selections/details from a different bound state are dropped, and a view with
  // no inspected captive starts on the first one.
  public override void Present(CampaignGameState state, GeoscapeSession session)
  {
    if (!ReferenceEquals(_state, state))
    {
      _selected.Clear();
      _details = null;
    }
    _state = state;

    SysColGeneric.IReadOnlyList<Combatant> captives = state.Captivity.Combatants;
    _selected.RemoveAll(selected =>
      !captives.AsValueEnumerable().Any(captive => ReferenceEquals(captive, selected)));
    if (_details is not null && !IsCaptive(_details, captives))
      _details = null;

    RebuildAll(captives);
  }

  // Opens interrogation preparation. Inert without selected captives (the control is
  // disabled for exactly that state); a configured squad view otherwise.
  public void PrepareInterrogation()
  {
    if (_selected.Count == 0)
      return;

    var view = SquadViewScene.InstantiateAs<SquadLoadoutView>(
      "CaptivityView SquadViewScene");
    view.Configure($"Interrogation", InterrogationCapacity,
      allowEquipmentEditing: false);
    RequestView(view);
  }

  private void RebuildAll(SysColGeneric.IReadOnlyList<Combatant> captives)
  {
    GetNode<Label>("%EmptyState").Visible = captives.Count == 0;
    if (captives.Count == 0)
      _details = null;
    else
      _details ??= captives[0]; // an uninspected screen starts on the first captive

    RebuildRows(captives);
    UpdateSelectionUi();
  }

  private void RebuildRows(SysColGeneric.IReadOnlyList<Combatant> captives)
  {
    var list = GetNode<VBoxContainer>("%CaptiveList");
    list.QueueFreeAllChildren();
    foreach (Combatant captive in captives)
    {
      var row = new Button
      {
        Text = $"{captive.Name} — {captive.OwningFaction.Name}",
        ToggleMode = true,
        CustomMinimumSize = new Vector2(0, 62),
        Alignment = HorizontalAlignment.Left,
        ClipText = true,
        AutowrapMode = TextServer.AutowrapMode.WordSmart,
        TooltipText = $"{captive.Name} — {captive.OwningFaction.Name}",
      };
      row.SetPressedNoSignal(IsSelected(captive));
      row.Toggled += pressed => OnCaptiveToggled(captive, pressed);
      list.AddChild(row);
    }
  }

  // Toggling inspects that captive in both directions and updates only the derived UI:
  // the native toggle already reflects the new state on the row, so rebuilding here would
  // destroy every row and the keyboard user's focus with it.
  private void OnCaptiveToggled(Combatant captive, bool pressed)
  {
    if (pressed)
    {
      if (!IsSelected(captive))
        _selected.Add(captive);
    }
    else
    {
      _selected.RemoveAll(selected => ReferenceEquals(selected, captive));
    }

    _details = captive;
    UpdateSelectionUi();
  }

  private void UpdateSelectionUi()
  {
    int count = _selected.Count;
    GetNode<Label>("%SelectionCount").Text = $"Selected: {count}";
    GetNode<Button>("%PrepareButton").Disabled = count == 0;
    GetNode<Control>("%CaptivePresentation").Visible = _details is not null;
    GetNode<RichTextLabel>("%Details").Text =
      _details is not null ? DetailsText(_details) : "";
  }

  private bool IsSelected(Combatant captive)
    => _selected.AsValueEnumerable().Any(selected => ReferenceEquals(selected, captive));

  private static bool IsCaptive(Combatant combatant,
    SysColGeneric.IReadOnlyList<Combatant> captives)
    => captives.AsValueEnumerable().Any(captive => ReferenceEquals(captive, combatant));

  private string DetailsText(Combatant captive) => string.Join("\n",
    $"Name: {captive.Name}",
    $"Faction: {captive.OwningFaction.Name}",
    CombatantSummary.StatsText(captive, "Health (max)", _state!.CampaignStatContributions(captive))
      .Replace("  ", "\n"),
    CombatantSummary.EquipmentText(captive, includeMods: false),
    BuffsText(captive));

  private static string BuffsText(Combatant captive)
  {
    SysColGeneric.List<string> names = [];
    foreach (var buff in captive.InnateBuffs)
      names.Add(buff.Name);
    return names.Count > 0 ? $"Buffs: {string.Join(", ", names)}" : "Buffs: — none —";
  }
}
