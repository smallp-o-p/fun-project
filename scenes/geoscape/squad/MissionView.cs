using CampaignGameState = global::FunProject.GameState.GameState;
using FunProject.Battle;
using FunProject.Combatants;
using FunProject.Scenes.Ext;
using FunProject.Strategic;
using Godot;
using System;

namespace FunProject.Geoscape;

// Owns mission preparation and the battle's full-screen viewport. Completion returns
// through the ordinary navigation signal.
public sealed partial class MissionView : SquadLoadoutView
{
  [Export] public PackedScene BattleScene { get; set; } = null!;

  private GeoscapeEvent _mission = null!;
  private GeoscapeSession _session = null!;
  private BattleScene? _battle;
  private MissionDeployment? _deployment;

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
    GetNode<Button>("%AbandonButton").Pressed += AbandonMission;
    GetNode<MissionResultsView>("%MissionResultsView").BackRequested += RequestBack;
  }

  public override void Present(CampaignGameState state, GeoscapeSession session)
  {
    _session = session;
    if (_deployment?.Receipt.Case is MissionReceipt receipt)
      ShowResults(receipt);
    else if (_battle is null)
    {
      BuildRewardPreview();
      base.Present(state, session);
    }
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

  private void BuildRewardPreview()
  {
    GetNode<Label>("%RewardPreview").Text = "Victory reward: " + (_mission.Definition.TacticalMission!.VictoryRewards.Count == 0
      ? "None" : string.Join(", ", _mission.Definition.TacticalMission.VictoryRewards.AsValueEnumerable()
        .Select(reward => $"{reward.Minimum}–{reward.Maximum} {reward.Currency.Name}").ToArray()));
  }

  private void DeployMission()
  {
    var host = BattleScene.InstantiateAs<BattleScene>("BattleScene");
    MissionBattle? launched = null;
    try
    {
      var launch = _session.LaunchMission(_mission, GetSelectedCombatants());
      launch.IfLeft(failure =>
      {
        GetNode<Label>("%MissionFailure").Text = failure.Message;
        host.Free();
      });
      launch.IfRight(handle =>
      {
        launched = handle;
        host.Present(handle.Runtime, handle.Setup, allowReturn: true);
        host.Completed += summary => CompleteMission(summary,
          handle.Runtime.Query(new GetBattleResultQuery()).Match(
            Right: result => result,
            Left: failure => throw new InvalidOperationException(failure.Message)));
        GetNode<SubViewport>("Background/Viewport").AddChild(host);
        host.InitializePresentation();
        GetNode<Control>("Content").Hide();
        GetNode<Control>("Background").Show();
        GetNode<Control>("%AbandonButton").Show();
        _session.ConfirmMissionDeployment(handle.Deployment);
        _deployment = handle.Deployment;
        _battle = host;
      });
    }
    catch (Exception failure)
    {
      host.Dispose();
      launched?.Runtime.Dispose();
      host.GetParent()?.RemoveChild(host);
      host.QueueFree();
      if (launched is not null) _session.AbortMissionPresentation();
      GetNode<Control>("Background").Hide();
      GetNode<Control>("%AbandonButton").Hide();
      GetNode<Control>("Content").Show();
      GetNode<Label>("%MissionFailure").Text = $"Deployment failed: {failure.Message}";
      throw;
    }
  }

  private void CompleteMission(FactionBattleSummary summary, BattleResult result)
  {
    try
    {
      _session.CompleteMission(_deployment!, summary, result);
    }
    finally
    {
      // The retained receipt remains usable even when a campaign subscriber throws.
      if (_deployment!.Receipt.Case is MissionReceipt receipt)
        ShowResults(receipt);
    }
  }

  private void ShowResults(MissionReceipt receipt)
  {
    _battle?.Dispose();
    GetNode<Control>("Background").Hide();
    GetNode<Control>("Content").Hide();
    GetNode<Control>("%AbandonButton").Hide();
    var view = GetNode<MissionResultsView>("%MissionResultsView");
    view.Bind(receipt);
    view.Show();
  }

  private void AbandonMission()
  {
    try { _session.AbandonMission(_deployment!); }
    finally
    {
      if (_deployment!.Receipt.Case is MissionReceipt receipt) ShowResults(receipt);
    }
  }
}
