using CampaignGameState = global::FunProject.GameState.GameState;
using FunProject.Battle;
using FunProject.Combatants;
using FunProject.Currencies;
using FunProject.Scenes;
using FunProject.Scenes.Ext;
using FunProject.Strategic;
using Godot;
using System;
using System.Collections.Generic;

namespace FunProject.Geoscape;

// Owns mission preparation and the battle's full-screen viewport. Completion returns
// through the ordinary navigation signal.
public sealed partial class MissionView : SquadLoadoutView
{
  [Export] public PackedScene BattleScene { get; set; } = null!;
  [Export] public PackedScene PredictionRowScene { get; set; } = null!;

  private GeoscapeEvent _mission = null!;
  private GeoscapeSession _session = null!;
  private CampaignGameState _state = null!;
  private readonly List<PredictionMarketRow> _predictionRows = [];
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
    GetNode<Button>("%PredictionMarketsButton").Pressed += () => ShowPredictionMarkets(true);
    GetNode<GeoscapeView>("%PredictionMarketPreparationView").BackRequested += () => ShowPredictionMarkets(false);
    GetNode<MissionResultsView>("%MissionResultsView").BackRequested += RequestBack;
  }

  private void ShowPredictionMarkets(bool show)
  {
    GetNode<Control>("Content").Visible = !show;
    GetNode<GeoscapeView>("%PredictionMarketPreparationView").Visible = show;
    UpdatePredictions();
  }

  public override void Present(CampaignGameState state, GeoscapeSession session)
  {
    _session = session;
    _state = state;
    if (_battle is null)
    {
      BuildPredictionMarkets();
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
    UpdatePredictions();
  }

  private void BuildPredictionMarkets()
  {
    if (_predictionRows.Count > 0) return;
    var view = GetNode<GeoscapeView>("%PredictionMarketPreparationView");
    view.GetNode<Label>("%MissionTitle").Text = _mission.Definition.Title;
    var rows = view.GetNode<VBoxContainer>("%PredictionMarketRows");
    foreach (var market in _mission.PredictionMarkets)
    {
      var row = PredictionRowScene.InstantiateAs<PredictionMarketRow>("PredictionRowScene");
      row.Configure(market, _state);
      row.Changed += UpdatePredictions;
      rows.AddChild(row);
      _predictionRows.Add(row);
    }
    view.GetNode<Label>("%PredictionMarketNotice").Text = _mission.PredictionMarkets.Count == 0 ? "No prediction markets available."
      : "Optional predictions: choose a currency and leave stake at 0 to skip. Correct predictions pay on any battle outcome. Total return includes your stake.";
  }

  private MissionPrediction[] SelectedPredictions()
  {
    var predictions = new List<MissionPrediction>();
    foreach (var row in _predictionRows) row.SelectedPrediction.IfSome(predictions.Add);
    return [.. predictions];
  }

  private void UpdatePredictions()
  {
    foreach (var row in _predictionRows) row.Refresh(_state);
    var costs = new Godot.Collections.Dictionary<Currency, ulong>();
    foreach (var prediction in SelectedPredictions())
    {
      costs.TryGetValue(prediction.Currency, out ulong amount);
      costs[prediction.Currency] = checked(amount + prediction.Amount);
    }
    bool affordable = _state.CanAffordCurrencyCosts(costs);
    GetNode<Button>("%DeployButton").Disabled = GetSelectedCombatants().Count == 0 || !affordable;
    string failure = affordable ? "" : "Insufficient currency for the combined stakes.";
    GetNode<Label>("%MissionFailure").Text = failure;
    GetNode<GeoscapeView>("%PredictionMarketPreparationView").GetNode<Label>("%PredictionMarketFailure").Text = failure;
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
        host.Completed += summary => CompleteMission(summary,
          handle.Runtime.Query(new GetBattleResultQuery()).Match(
            Right: result => result,
            Left: failure => throw new InvalidOperationException(failure.Message)));
        GetNode<SubViewport>("Background/Viewport").AddChild(host);
        host.InitializePresentation();
        GetNode<Control>("Content").Hide();
        GetNode<Control>("Background").Show();
        if (!_session.TryAcceptMissionPredictions(SelectedPredictions()))
          throw new InvalidOperationException("Insufficient currency for the stakes.");
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
      GetNode<Control>("Content").Show();
      GetNode<Label>("%MissionFailure").Text = $"Deployment failed: {failure.Message}";
      throw;
    }
  }

  private void CompleteMission(FactionBattleSummary summary, BattleResult result)
  {
    var results = GetNode<MissionResultsView>("%MissionResultsView");
    try
    {
      _session.CompleteMission(summary);
    }
    finally
    {
      if (_session.ActiveMission.IsNone)
      {
        results.Bind(_mission.Definition.TacticalMission!.BattleType.Name, summary, result,
          summary.Outcome == BattleOutcome.Victory ? _mission.Rewards : new Dictionary<Currency, ulong>(), _mission.Predictions);
        _battle?.Dispose();
        GetNode<Control>("Background").Hide();
        results.Show();
      }
    }
  }

  public override void _ExitTree()
  {
    if (_battle is not null && _session.ActiveMission.IsSome)
      _session.AbortMissionPresentation();
  }
}
