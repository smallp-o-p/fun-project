using FunProject.Strategic;
using Godot;
using System;

public sealed partial class GeoscapeEventResolution : Control
{
  public event Action<ResolutionOutcome>? Resolved;

  private Label _title = null!;
  private Label _description = null!;
  private HBoxContainer _buttons = null!;

  public override void _Ready()
  {
    _title = GetNode<Label>("%Title");
    _description = GetNode<Label>("%Description");
    _buttons = GetNode<HBoxContainer>("%Buttons");
    Visible = false;
  }

  public void Present(PendingResolution pending, string regionSuffix)
  {
    _title.Text = pending.Event.Definition.Title;
    _description.Text = $"[{pending.Event.Definition.Kind}{regionSuffix}]\n{pending.Event.Definition.Description}";

    foreach (var (txt, outcome) in ButtonsFor(pending.Event.Definition.Kind))
    {
      var button = new Button { Text = txt };
      button.Pressed += () => Resolved?.Invoke(outcome);
      _buttons.AddChild(button);
    }

    Visible = true;
  }

  public void Dismiss()
  {
    _title.Text = string.Empty;
    _description.Text = string.Empty;
    Visible = false;

    foreach (Node child in _buttons.GetChildren())
    {
      _buttons.RemoveChild(child);
      child.QueueFree();
    }
  }

  private static (string Text, ResolutionOutcome Outcome)[] ButtonsFor(GeoscapeEventKind kind)
  {
    return kind switch
    {
      // Engage is the future scene-swap point: a screen manager would build a MapBattleSetup
      // and swap scenes instead of just resolving.
      GeoscapeEventKind.TacticalBattle =>
        [("Engage", ResolutionOutcome.Engaged), ("Decline", ResolutionOutcome.Declined)],
      GeoscapeEventKind.Minigame => [("Play (placeholder)", ResolutionOutcome.Acknowledged)],
      _ => [("Continue", ResolutionOutcome.Acknowledged)],
    };
  }
}
