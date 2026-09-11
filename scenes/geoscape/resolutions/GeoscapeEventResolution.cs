using CampaignGameState = global::FunProject.GameState.GameState;
using FunProject.Strategic;
using Godot;
using System;

// The pending-resolution modal. A transparent GeoscapeView over the covered stack: the
// base shell's background stays hidden and the Dim wash blocks the map/HUD beneath. The
// composition root pushes a fresh instance on ResolutionEventOpened and pops it on the
// session's committed ResolutionEventClosed — buttons resolve through the session, so
// Back navigation never applies while a resolution is pending.
public sealed partial class GeoscapeEventResolution : GeoscapeView
{
  public event Action<ResolutionOutcome>? Resolved;

  private Label _title = null!;
  private Label _description = null!;
  private HBoxContainer _buttons = null!;

  public override void _Ready()
  {
    base._Ready();
    _title = GetNode<Label>("%Title");
    _description = GetNode<Label>("%Description");
    _buttons = GetNode<HBoxContainer>("%Buttons");
  }

  // Renders the session's pending resolution; pushed only while one is open, so a missing
  // pending resolution is a caller bug.
  public override void Present(CampaignGameState state, GeoscapeSession session)
  {
    PendingResolution pending = session.PendingResolution.Match(
      value => value,
      () => throw new InvalidOperationException(
        "GeoscapeEventResolution requires a pending resolution; it is presented only while one is open."));

    foreach (Node child in _buttons.GetChildren())
    {
      _buttons.RemoveChild(child);
      child.QueueFree();
    }

    _title.Text = pending.Event.Definition.Title;
    _description.Text =
      $"[{pending.Event.Definition.Kind}{RegionSuffix(session, pending)}]\n{pending.Event.Definition.Description}";

    foreach (var (txt, outcome) in ButtonsFor(pending.Event.Definition.Kind))
    {
      var button = new Button { Text = txt };
      button.Pressed += () => Resolved?.Invoke(outcome);
      _buttons.AddChild(button);
    }
  }

  private static string RegionSuffix(GeoscapeSession session, PendingResolution pending)
  {
    return pending.Event.TargetRegionIndex.Match(
      index => $" — {session.Regions[index].Name}",
      () => "");
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
