using CampaignGameState = FunProject.GameState.GameState;
using FunProject.Combatants;
using FunProject.Strategic;
using Godot;
using System;
using FunProject.Scenes.Ext;

/// <summary>
/// Display the list of units the player has.
/// </summary>
public sealed partial class UnitRoster : GeoscapeView
{
  [Export] public PackedScene? UnitLabelScene { get; set; }
  [Export] public PackedScene? UnitViewScene { get; set; }

  private VBoxContainer _unitLabels = null!;

  public override void _Ready()
  {
    base._Ready();
    _unitLabels = GetNode<VBoxContainer>("%UnitLabels");
  }

  public override void Present(CampaignGameState state, GeoscapeSession session)
  {
    _unitLabels.QueueFreeAllChildren();

    PackedScene scene = UnitLabelScene ?? throw new InvalidOperationException(
      "UnitRoster requires UnitLabelScene; assign a PackedScene in the inspector.");
    foreach (Combatant unit in state.Roster)
    {
      var label = scene.Instantiate<UnitLabel>();
      _unitLabels.AddChild(label);
      label.Bind(unit);
      label.Pressed += () => OpenUnit(unit);
    }
  }

  // The roster owns its inspect-mode wiring: no selected-unit state leaks upward.
  private void OpenUnit(Combatant unit)
  {
    var view = UnitViewScene!.InstantiateAs<UnitView>();
    view.BindUnit(unit); // retain the selection before the view enters the tree
    RequestView(view);
  }
}
