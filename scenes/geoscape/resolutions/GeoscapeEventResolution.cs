using CampaignGameState = global::FunProject.GameState.GameState;
using FunProject.Strategic;
using Godot;
using System;
using FunProject.Geoscape;
using FunProject.Scenes.Ext;

/// <summary>
/// Modal dialog to resolve a GeoscapeEvent that needs addressing.
/// </summary>
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

  public override void Present(CampaignGameState state, GeoscapeSession session)
  {
    PendingResolution pending = session.PendingResolution.Match(
      value => value,
      () => throw new InvalidOperationException(
        "GeoscapeEventResolution requires a pending resolution; it is presented only while one is open."));
    string heading = pending.Event.Definition.Title;
    _buttons.QueueFreeAllChildren();

    _title.Text = heading;
    _description.Text =
      $"[{pending.Event.Definition.Kind}{RegionSuffix(session, pending)}]\n{pending.Event.Definition.Description}";

    foreach (var (txt, outcome) in ButtonsFor(pending.Event.Definition.Kind))
    {
      var button = new Button { Text = txt };
      if (outcome == ResolutionOutcome.Engaged)
      {
        button.Pressed += () => RequestSquadView(pending.Event.Definition);
      }
      else
      {
        button.Pressed += () => Resolved?.Invoke(outcome);
      }
      _buttons.AddChild(button);
    }
  }

  private void RequestSquadView(GeoscapeEventDefinition mission)
  {
    var view = SquadViewScene!.InstantiateAs<SquadLoadoutView>();
    view.Configure(mission.Title, 3, allowEquipmentEditing: true, mission: mission);
    RequestView(view);
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
