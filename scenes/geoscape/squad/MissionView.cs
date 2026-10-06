using CampaignGameState = global::FunProject.GameState.GameState;
using FunProject.Battle;
using FunProject.Combatants;
using FunProject.Scenes.Ext;
using FunProject.Strategic;
using Godot;
using System;
using System.Collections.Generic;

namespace FunProject.Geoscape;

// One pushed view owns preparation and its live battle. Covered equipment views return to
// the same selection; completing the battle returns through the ordinary navigation signal.
public sealed partial class MissionView : SquadLoadoutView
{
  [Export] public PackedScene BattleScene { get; set; } = null!;

  private GeoscapeSession _session = null!;
  private BattleScene? _battle;

  public override void _Ready()
  {
    base._Ready();
    DeployRequested += DeployMission;
  }

  public override void Present(CampaignGameState state, GeoscapeSession session)
  {
    _session = session;
    if (_battle is null)
      base.Present(state, session);
  }

  private void DeployMission(GeoscapeEvent mission, IReadOnlyList<Combatant> squad)
  {
    if (_battle is not null)
      throw new InvalidOperationException("The mission is already presented.");

    var host = BattleScene.InstantiateAs<BattleScene>("BattleScene");
    MissionBattle? launched = null;
    try
    {
      var launch = _session.LaunchMission(mission, squad);
      launch.IfLeft(failure =>
      {
        ShowDeploymentFailure(failure);
        host.Free();
      });
      launch.IfRight(handle =>
      {
        launched = handle;
        host.Present(handle.Runtime, handle.Setup, allowReturn: true);
        host.Completed += CompleteMission;
        AddChild(host);
        host.InitializePresentation();
        GetNode<Control>("Content").Hide();
        _battle = host;
      });
    }
    catch
    {
      host.Dispose();
      if (host.IsInsideTree())
        RemoveChild(host);
      host.QueueFree();
      if (launched is not null)
        _session.AbortMissionPresentation();
      throw;
    }
  }

  private void CompleteMission(FactionBattleSummary summary)
  {
    try
    {
      _session.CompleteMission(summary);
    }
    finally
    {
      // Campaign consumption precedes notifications. A subscriber failure still pops the
      // view and disposes the host; a failure before consumption leaves it for a retry.
      if (_session.ActiveMission.IsNone)
        RequestBack();
    }
  }

  public override void _ExitTree()
  {
    if (_battle is not null && _session.ActiveMission.IsSome)
      _session.AbortMissionPresentation();
  }
}
