using Godot;
using System;

namespace FunProject.Geoscape;

// One authored squad slot card: the loadout view instantiates this per slot, wires the
// button events in code, and pushes display state through Bind. Pure presentation — the
// view computes every string; the card owns only the empty-slot placeholder copy, like
// UnitLabel owns its status placeholder.
public partial class SquadSlotCard : PanelContainer
{
  public Label? CardTitle { get; private set; }
  public Button? EditUnit { get; private set; }
  public Button? ChooseUnit { get; private set; }
  public Button? RemoveUnit { get; private set; }
  public RichTextLabel? Equipment { get; private set; }
  public RichTextLabel? Stats { get; private set; }

  public event Action? EditRequested;
  public event Action? ChooseRequested;
  public event Action? RemoveRequested;

  private const string NoUnitAssigned = "No unit assigned.";

  // Bind follows tree entry (as with UnitLabel): the %nodes resolve in _Ready, so callers
  // add the card to a container before pushing state through this single door. Editing
  // can be turned off for read-only uses (interrogation preparation hides the Edit button).
  public void Bind(string title, bool occupied, string equipmentText, string statsText,
    bool allowEquipmentEditing = true)
  {
    CardTitle!.Text = title;
    EditUnit!.Visible = occupied && allowEquipmentEditing;
    RemoveUnit!.Visible = occupied;
    ChooseUnit!.Text = occupied ? "Replace" : "Choose";
    Equipment!.Text = occupied ? equipmentText : NoUnitAssigned;
    Stats!.Text = occupied ? statsText : "";
  }

  public override void _Ready()
  {
    CardTitle = GetNode<Label>("%CardTitle");
    EditUnit = GetNode<Button>("%EditUnit");
    ChooseUnit = GetNode<Button>("%ChooseUnit");
    RemoveUnit = GetNode<Button>("%RemoveUnit");
    Equipment = GetNode<RichTextLabel>("%Equipment");
    Stats = GetNode<RichTextLabel>("%Stats");

    EditUnit.Pressed += () => EditRequested?.Invoke();
    ChooseUnit.Pressed += () => ChooseRequested?.Invoke();
    RemoveUnit.Pressed += () => RemoveRequested?.Invoke();
  }
}
