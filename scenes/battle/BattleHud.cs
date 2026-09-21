using FunProject.Battle;
using Godot;
using System;
using System.Collections.Generic;
using FunProject.Scenes.Ext;

/// <summary>
/// Battle HUD widgets. Buttons raise intent events here; BattleScene subscribes to those events
/// and pushes state updates to be presented.
/// </summary>
public sealed partial class BattleHud : CanvasLayer
{
  private VBoxContainer _verbButtons = null!;
  private Button _confirmButton = null!;
  private Button _cancelButton = null!;
  private Label _unitStatusLabel = null!;
  private Label _hitChanceLabel = null!;
  private Label _bannerLabel = null!;

  public event Action? ConfirmRequested;
  public event Action? CancelRequested;
  public event Action<UnitActionOption>? VerbSelected;

  public override void _Ready()
  {
    _verbButtons = GetNode<VBoxContainer>("%VerbButtons");
    _confirmButton = GetNode<Button>("%ConfirmButton");
    _cancelButton = GetNode<Button>("%CancelButton");
    _unitStatusLabel = GetNode<Label>("%UnitStatus");
    _hitChanceLabel = GetNode<Label>("%HitChance");
    _bannerLabel = GetNode<Label>("%BattleOverBanner");

    _confirmButton.Pressed += () => ConfirmRequested?.Invoke();
    _cancelButton.Pressed += () => CancelRequested?.Invoke();
  }

  public void ShowUnits(IReadOnlyList<BattleUnitState> units)
    => _unitStatusLabel.Text = string.Join("\n", units.AsValueEnumerable().Select(FormatUnitReadout).ToArray());

  public void ShowActionOptions(IReadOnlyList<UnitActionOption> options)
  {
    _verbButtons.QueueFreeAllChildren();

    foreach (UnitActionOption option in options)
    {
      var button = new Button
      {
        Text = LabelFor(option),
        Disabled = !option.IsAvailable,
      };
      UnitActionOption captured = option;
      button.Pressed += () => VerbSelected?.Invoke(captured);
      _verbButtons.AddChild(button);
    }
  }

  public void ShowHitChance(int finalChance)
  {
    _hitChanceLabel.Text = $"{finalChance}%";
    _hitChanceLabel.Visible = true;
  }

  public void HideHitChance()
    => _hitChanceLabel.Visible = false;

  public void ShowBattleOver(string bannerText)
  {
    _bannerLabel.Text = bannerText;
    _bannerLabel.Visible = true;
  }

  internal static string FormatUnitReadout(BattleUnitState unit)
    => $"{unit.Combatant.Name}  HP {unit.CurrentHealth}/{unit.MaxHealth}  STUN {unit.CurrentStun}"
      + (unit.IsUnconscious ? "  Unconscious" : "");

  private static string LabelFor(UnitActionOption option) => option switch
  {
    MoveActionOption => "Move",
    AttackActionOption => "Attack",
    PassActionOption => "Pass",
    EndTurnActionOption => "End Turn",
    ReloadActionOption => "Reload",
    _ => option.GetType().Name,
  };
}
