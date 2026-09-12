using CampaignGameState = global::FunProject.GameState.GameState;
using FunProject.Combatants;
using FunProject.Strategic;
using Godot;
using System;
using FunProject.Scenes.Ext;

// Full-screen roster view over the campaign state. Presentation only: every activation
// rebuilds rows from GameState.Roster (query, not copy). A row click builds a fresh
// UnitView, binds the selected combatant before it enters the tree, and requests it
// through the navigation signal — the manager owns the stack, the composition root
// presents the view once it is active.
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
    // ClearRows strips the authored placeholder rows on first present (and remains
    // idempotent thereafter); every present then rebuilds from the current roster.
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
    PackedScene scene = UnitViewScene ?? throw new InvalidOperationException(
      "UnitRoster requires UnitViewScene; assign a PackedScene in the inspector.");
    Node instance = scene.Instantiate();
    if (instance is not UnitView view)
    {
      instance.Free(); // free now: rejected roots must not linger to frame end
      throw new InvalidOperationException(
        "UnitRoster requires UnitViewScene whose root is a UnitView.");
    }
    view.BindUnit(unit); // retain the selection before the view enters the tree
    RequestView(view);
  }
}
