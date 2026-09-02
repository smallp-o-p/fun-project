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
  public event Action<Combatant>? UnitSelected;

  private VBoxContainer _unitLabels = null!;
  private Action? _requestClose;

  public void ArmClose(Action requestClose) => _requestClose = requestClose;

  public override void _Ready()
  {
    _unitLabels = GetNode<VBoxContainer>("%UnitLabels");
    GetNode<Button>("%BackButton").Pressed += OnBackPressed;
  }

  private void OnBackPressed() => _requestClose?.Invoke();

  public void Present(FunProject.GameState.GameState state)
  {
    // ClearRows strips the authored placeholder rows on first present (and remains
    // idempotent thereafter); every present then rebuilds from the current roster.
    ClearRows();

    PackedScene scene = UnitLabelScene ?? throw new InvalidOperationException(
      "UnitRoster requires UnitLabelScene; assign a PackedScene in the inspector.");
    foreach (Combatant unit in state.Roster)
    {
      var label = scene.Instantiate<UnitLabel>();
      _unitLabels.AddChild(label);
      label.Bind(unit);
      label.Pressed += () => UnitSelected?.Invoke(unit);
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
