using FunProject.Combatants;
using Godot;
using System;

// Full-screen roster view over the campaign state. Presentation only: every Present
// rebuilds rows from GameState.Roster (query, not copy — the on-demand lifecycle
// guarantees a fresh read on every open). The future inspect-mode parameters arrive
// through this same entry point.
public sealed partial class UnitRoster : PanelContainer, IGeoscapeView
{
  [Export] public PackedScene? UnitLabelScene { get; set; }

  private VBoxContainer _unitLabels = null!;
  private bool _presentedOnce;

  public event Action? Closed;

  public override void _Ready()
  {
    _unitLabels = GetNode<VBoxContainer>("%UnitLabels");
    GetNode<Button>("%BackButton").Pressed += () => Closed?.Invoke();
  }

  public void Present(FunProject.GameState.GameState state)
  {
    // First present strips the authored placeholder rows (they preview layout in the
    // editor only); every present then rebuilds from the current roster.
    ClearRows();
    _presentedOnce = true;

    foreach (Combatant unit in state.Roster)
    {
      PackedScene scene = UnitLabelScene ?? throw new InvalidOperationException(
        "UnitRoster requires UnitLabelScene; assign a PackedScene in the inspector.");
      var label = scene.Instantiate<UnitLabel>();
      _unitLabels.AddChild(label);
      label.Bind(unit);
    }
  }

  private void ClearRows()
  {
    foreach (Node child in _unitLabels.GetChildren())
    {
      _unitLabels.RemoveChild(child); // detach now: replaced rows must not linger to frame end
      child.QueueFree();
    }
  }
}
