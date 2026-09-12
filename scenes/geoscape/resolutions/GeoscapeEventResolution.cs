using CampaignGameState = global::FunProject.GameState.GameState;
using FunProject.Strategic;
using Godot;
using System;
using FunProject.Scenes.Ext;

// The pending-resolution modal. A transparent GeoscapeView over the covered stack: the
// base shell's background stays hidden and the Dim wash blocks the map/HUD beneath. The
// composition root pushes a fresh instance on ResolutionEventOpened and pops it on the
// session's committed ResolutionEventClosed — so the dialog carries no back button and a
// stray close (top not the dialog) pops nothing. Outcome buttons resolve through the
// session; tactical Engage instead produces the pre-mission squad view from the exported
// SquadViewScene and requests it as the next stacked view, so the mission stays pending.
public sealed partial class GeoscapeEventResolution : GeoscapeView
{
  [Export] public PackedScene? SquadViewScene { get; set; }

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
    
    _buttons.QueueFreeAllChildren();

    _title.Text = pending.Event.Definition.Title;
    _description.Text =
      $"[{pending.Event.Definition.Kind}{RegionSuffix(session, pending)}]\n{pending.Event.Definition.Description}";

    foreach (var (txt, outcome) in ButtonsFor(pending.Event.Definition.Kind))
    {
      var button = new Button { Text = txt };
      if (outcome == ResolutionOutcome.Engaged)
      {
        button.Pressed += () => RequestView(SquadViewScene);
      }
      else
      {
        button.Pressed += () => Resolved?.Invoke(outcome);
      }
      _buttons.AddChild(button);
    }
  }

  public void RequestView(PackedScene? scene)
  {
    PackedScene target = scene ?? throw new InvalidOperationException(
      "GeoscapeEventResolution RequestView requires a PackedScene destination.");
    Node instance = target.Instantiate();
    if (instance is not GeoscapeView view)
    {
      string kind = instance.GetClass();
      instance.Free();
      throw new InvalidOperationException(
        $"GeoscapeEventResolution RequestView destination root must be a GeoscapeView; got {kind}.");
    }
    EmitSignal(GeoscapeView.SignalName.ViewRequested, view);
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
      GeoscapeEventKind.TacticalBattle =>
        [("Engage", ResolutionOutcome.Engaged), ("Decline", ResolutionOutcome.Declined)],
      GeoscapeEventKind.Minigame => [("Play (placeholder)", ResolutionOutcome.Acknowledged)],
      _ => [("Continue", ResolutionOutcome.Acknowledged)],
    };
  }
}
