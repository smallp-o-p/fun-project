using CampaignGameState = global::FunProject.GameState.GameState;
using FunProject.Battle;
using FunProject.Combatants;
using FunProject.Scenes.Ext;
using FunProject.Strategic;
using Godot;

namespace FunProject.Geoscape;

// Owns mission preparation and the battle's full-screen viewport. Completion returns
// through the ordinary navigation signal.
public sealed partial class MissionView : SquadLoadoutView
{
  [Export] public PackedScene BattleScene { get; set; } = null!;

  private GeoscapeEvent _mission = null!;
  private GeoscapeSession _session = null!;
  private BattleScene? _battle;

  public void ConfigureMission(GeoscapeEvent mission)
  {
    _mission = mission;
    Configure(mission.Definition.Title, mission.Definition.TacticalMission!.Size.MaxPlayerUnits,
      allowEquipmentEditing: true);
  }

  public override void _Ready()
  {
    base._Ready();
    GetNode<Button>("%DeployButton").Pressed += DeployMission;
  }

  public override void Present(CampaignGameState state, GeoscapeSession session)
  {
    _session = session;
    if (_battle is null)
      base.Present(state, session);
  }

  protected override bool CanChoose(Combatant unit) => _session.CanDeploy(unit, _mission.Definition);

  protected override void RebuildAll()
  {
    base.RebuildAll();
    if (_mission.Definition.AllowUnfitDeployment)
      GetNode<Label>("%SelectionPrompt").Text +=
        "\nExceptional deployment authorized — injured or exhausted units may join this mission.";
    GetNode<Button>("%DeployButton").Disabled = GetSelectedCombatants().Count == 0;
  }

  private void DeployMission()
  {
    var host = BattleScene.InstantiateAs<BattleScene>("BattleScene");
    MissionBattle? launched = null;
    try
    {
      var launch = _session.LaunchMission(_mission, GetSelectedCombatants());
      launch.IfLeft(_ => host.Free());
      launch.IfRight(handle =>
      {
        launched = handle;
        host.Present(handle.Runtime, handle.Setup, allowReturn: true);
        host.Completed += CompleteMission;
        GetNode<SubViewport>("Background/Viewport").AddChild(host);
        host.InitializePresentation();
        GetNode<Control>("Content").Hide();
        GetNode<Control>("Background").Show();
        _battle = host;
      });
    }
    catch
    {
      host.Dispose();
      host.GetParent()?.RemoveChild(host);
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
      // Consumption completes before notifications; committed returns pop even when a subscriber throws.
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
