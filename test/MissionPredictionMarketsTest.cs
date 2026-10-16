using System;
using System.Threading.Tasks;
using FunProject.Geoscape;
using Godot;
using static FunProject.Tests.GeoscapeTestScenes;
using FunProject.Battle;
using FunProject.Currencies;
using RosterEntryData = FunProject.GameState.RosterEntryData;
using CampaignStartData = FunProject.GameState.CampaignStartData;
using FunProject.Items.Effects;
using FunProject.Strategic;
using GdUnit4;

namespace FunProject.Tests;

[TestSuite]
[RequireGodotRuntime]
public class MissionPredictionMarketsTest
{
  private static GeoscapeFixture Campaign(Currency currency, BattleOutcome outcome = BattleOutcome.Victory,
    ulong balance = 1000, int? seed = null, Action<TacticalMissionData>? customize = null,
    RosterEntryData[]? roster = null, bool rewards = true, Action<CampaignStartData>? configure = null)
  {
    var mission = MakeTacticalMission(MakeTacticalBattleType(playerCells: 2, enemyCells: 2));
    mission.BattleType.Factions[0].Objectives.Clear();
    mission.BattleType.Factions[0].Objectives.Add(new EliminateAllOpposingForcesObjectiveData
    {
      OnComplete = new EndBattleDirectiveData { Outcome = outcome },
      OnFail = new EndBattleDirectiveData { Outcome = BattleOutcome.Defeat },
    });
    customize?.Invoke(mission);
    var start = MakeStart(roster: roster ?? [MakeEntry("Alpha")],
      startingCurrencies: new() { [currency] = balance },
      timeline: [MakeScheduled(1, MakeEvent("Reward mission", GeoscapeEventKind.TacticalBattle, tacticalMission: mission))]);
    start.PredictionMarketPool.Add(new DeployUnitPredictionMarketData { MaxStake = 100, Multiplier = 1.5 });
    start.PredictionMarketPool.Add(new UnitKillsPredictionMarketData
    {
      MaxStake = 200,
      Multiplier = 2.5,
      MinimumKills = 1,
      MaximumKills = 3,
    });
    if (rewards) start.MissionRewardPool.Add(new MissionCurrencyRewardData { Currency = currency, Minimum = 50, Maximum = 50 });
    configure?.Invoke(start);
    var campaign = new GeoscapeFixture(start);
    campaign.AdvanceTicks(1);
    if (seed is int value) campaign.State.ActiveEvents[0] = campaign.ActiveEvent with
    {
      BattleSeed = Some(value),
      PredictionMarkets = campaign.State.GenerateMissionPredictionMarkets(value),
    };
    campaign.OpenResolution(campaign.ActiveEvent);
    return campaign;
  }

  private static FactionBattleSummary Win(MissionBattle battle, GeoscapeFixture campaign, int shooterX = 0)
  {
    var unit = battle.Runtime.Query(new GetUnitAtTile(battle.Runtime.TryGetTile(new Vector3I(shooterX, 0, 0)).RequireSome())).RequireSome();
    var grenade = MakeGrenade("Frag", throwRange: 10, blastRadius: 0,
      effects: [new DamageEffectData { BaseDamage = 999 }]);
    unit.AddInventoryItem(grenade.Item);
    battle.Runtime.ExecuteAction(BattleAction.ThrowItem(battle.Runtime.TryGetAlive(unit).RequireSome(), grenade,
      battle.Runtime.TryGetTile(new Vector3I(2, 0, 0)).RequireSome()));
    return battle.Runtime.Query(new GetFactionEndOfBattleSummary(campaign.State.PlayerFaction)).RequireRight();
  }

  [TestCase(BattleOutcome.Victory, 1000UL, 1099UL, 1148UL, true)]
  [TestCase(BattleOutcome.Defeat, 1000UL, 1049UL, 1148UL, true)]
  [TestCase(BattleOutcome.Draw, 1000UL, 1049UL, 1148UL, true)]
  [TestCase(BattleOutcome.Victory, 1000UL, 1099UL, 901UL, false)]
  [TestCase(BattleOutcome.Victory, ulong.MaxValue, ulong.MaxValue, ulong.MaxValue, true)]
  public static void SettlementPaysSelectedCurrenciesOnceBeforeNotifications(BattleOutcome outcome,
    ulong balance, ulong expectedA, ulong expectedB, bool killPredictionCorrect)
  {
    var a = MakeCurrency("Credits");
    var b = MakeCurrency("Intel");
    using var campaign = Campaign(a, outcome, balance, seed: 1, configure: start =>
    {
      start.StartingCurrencies[b] = balance;
      var kills = (UnitKillsPredictionMarketData)start.PredictionMarketPool[1];
      kills.MinimumKills = kills.MaximumKills = killPredictionCorrect ? 1U : 2U;
    });
    MissionPrediction[] predictions =
      [new(campaign.ActiveEvent.PredictionMarkets[0], a, 99), new(campaign.ActiveEvent.PredictionMarkets[1], b, 99)];
    var battle = campaign.Session.LaunchMission(campaign.ActiveEvent, campaign.State.Roster).RequireRight();
    using var runtime = battle.Runtime;
    Assert.Equal(balance, campaign.State.GetCurrencyBalance(a));
    Assert.Equal(balance, campaign.State.GetCurrencyBalance(b));
    Assert.True(campaign.Session.TryAcceptMissionPredictions(predictions));
    Assert.True(campaign.Session.TryAcceptMissionPredictions(predictions));
    Assert.Equal(balance - 99, campaign.State.GetCurrencyBalance(a));
    Assert.Equal(balance - 99, campaign.State.GetCurrencyBalance(b));
    var summary = Win(battle, campaign);
    var rebuilt = new GeoscapeSession(campaign.State);
    rebuilt.EventCommitted += _ =>
    {
      Assert.Equal(expectedA, campaign.State.GetCurrencyBalance(a));
      Assert.Equal(expectedB, campaign.State.GetCurrencyBalance(b));
      Assert.True(rebuilt.ActiveMission.IsNone);
      Assert.True(rebuilt.PendingResolution.IsNone);
      rebuilt.CompleteMission(summary);
      throw new InvalidOperationException("Notification failed");
    };
    Assert.Throws<InvalidOperationException>(() => rebuilt.CompleteMission(summary));
    Assert.True(battle.Deployment.Predictions[0].Correct);
    Assert.Equal(killPredictionCorrect, battle.Deployment.Predictions[1].Correct);
    Assert.Equal(148UL, battle.Deployment.Predictions[0].Returned);
    Assert.Equal(killPredictionCorrect ? 247UL : 0UL, battle.Deployment.Predictions[1].Returned);
    Assert.True(campaign.State.TrySpendCurrencyCosts(new() { [a] = 1, [b] = 1 }));
    rebuilt.CompleteMission(summary);
    Assert.Equal(expectedA - 1, campaign.State.GetCurrencyBalance(a));
    Assert.Equal(expectedB - 1, campaign.State.GetCurrencyBalance(b));
  }

  [TestCase]
  public static void AuthoredZeroKillPredictionWinsWithoutAKillSummaryEntry()
  {
    var currency = MakeCurrency();
    using var campaign = Campaign(currency, customize: mission =>
    {
      mission.BattleType.Factions[0].Objectives.Clear();
      mission.BattleType.Factions[0].Objectives.Add(new SurviveUntilTurnObjectiveData
      {
        TargetTurn = 1,
        OnComplete = new EndBattleDirectiveData { Outcome = BattleOutcome.Victory },
      });
    }, configure: start =>
    {
      var kills = (UnitKillsPredictionMarketData)start.PredictionMarketPool[1];
      kills.MinimumKills = kills.MaximumKills = 0;
    });
    var market = (UnitKillsPredictionMarket)campaign.ActiveEvent.PredictionMarkets[1];
    var battle = campaign.Session.LaunchMission(campaign.ActiveEvent, campaign.State.Roster).RequireRight();
    using var runtime = battle.Runtime;
    Assert.True(campaign.Session.TryAcceptMissionPredictions([new(market, currency, 100)]));
    var summary = runtime.Query(new GetFactionEndOfBattleSummary(campaign.State.PlayerFaction)).RequireRight();
    Assert.False(summary.DefeatedPerCombatant.ContainsKey(market.Unit));
    campaign.Session.CompleteMission(summary);
    Assert.True(battle.Deployment.Predictions[0].Correct);
    Assert.Equal(1200UL, campaign.State.GetCurrencyBalance(currency));
  }

  [TestCase]
  public static void RejectedSetupAndLateUnaffordabilityDoNotChargeAnyCurrency()
  {
    var a = MakeCurrency("Credits");
    var b = MakeCurrency("Intel");
    using var campaign = Campaign(a, customize: mission => mission.BattleType.MapPool.Clear(),
      configure: start => start.StartingCurrencies[b] = 1000);
    var first = new MissionPrediction(campaign.ActiveEvent.PredictionMarkets[0], a, 100);
    Assert.Equal(BattleSetupFailureReason.EmptyMapPool,
      campaign.Session.LaunchMission(campaign.ActiveEvent, campaign.State.Roster).RequireLeft().Reason);
    Assert.Equal(1000UL, campaign.State.GetCurrencyBalance(a));
    campaign.ActiveEvent.Definition.TacticalMission!.BattleType.MapPool.Add(MakeTacticalBattleType(playerCells: 2, enemyCells: 2).MapPool[0]);
    var battle = campaign.Session.LaunchMission(campaign.ActiveEvent, campaign.State.Roster).RequireRight();
    using var runtime = battle.Runtime;
    Assert.True(campaign.State.TrySpendCurrencyCosts(new() { [a] = 750, [b] = 899 }));
    Assert.False(campaign.Session.TryAcceptMissionPredictions(
      [first, new(campaign.ActiveEvent.PredictionMarkets[1], b, 200)]));
    Assert.Equal(250UL, campaign.State.GetCurrencyBalance(a));
    Assert.Equal(101UL, campaign.State.GetCurrencyBalance(b));
    Assert.False(campaign.Session.TryAcceptMissionPredictions(
      [first, new(campaign.ActiveEvent.PredictionMarkets[1], a, 200)]));
    Assert.Equal(250UL, campaign.State.GetCurrencyBalance(a));
    Assert.True(campaign.State.TrySpendCurrencyCosts(new() { [a] = 200 }));
    Assert.False(campaign.Session.TryAcceptMissionPredictions([first]));
    Assert.Equal(50UL, campaign.State.GetCurrencyBalance(a));
    Assert.Equal(101UL, campaign.State.GetCurrencyBalance(b));
    Assert.Equal(0, battle.Deployment.Predictions.Count);
    campaign.Session.AbortMissionPresentation();
    Assert.True(campaign.Session.PendingResolution.IsSome);
  }

  [TestCase(800, 600)]
  [TestCase(1600, 900)]
  public static async Task PreparationRetainsCurrenciesAndDraftsAndDisplaysAcceptedOutcomes(int width, int height)
  {
    await using var cleanup = new DeferredNodeCleanup();
    var a = MakeCurrency("Credits");
    var b = MakeCurrency("Intel");
    using var campaign = Campaign(a, balance: 300, seed: 3, customize: mission =>
    {
      mission.BattleType.Factions[0].Objectives.Clear();
      mission.BattleType.Factions[0].Objectives.Add(new SurviveUntilTurnObjectiveData
      {
        TargetTurn = 1,
        OnComplete = new EndBattleDirectiveData { Outcome = BattleOutcome.Victory },
      });
    });
    campaign.State.CreditCurrencies(new SysColGeneric.Dictionary<Currency, ulong> { [b] = 200 });
    var view = CreateMissionView();
    CreateScaledWindow(view, new Vector2I(width, height));
    view.ConfigureMission(campaign.ActiveEvent);
    view.Present(campaign.State, campaign.Session);
    ChooseSquadUnit(view, 0, "Alpha");
    await WaitForLayout(view);
    var markets = view.GetNode<FunProject.Scenes.GeoscapeView>("%PredictionMarketPreparationView");
    Assert.False(markets.Visible);
    Assert.True(ScreenRect(view).Encloses(ScreenRect(view.GetNode<Button>("%PredictionMarketsButton"))));
    view.GetNode<Button>("%PredictionMarketsButton").EmitSignal(Button.SignalName.Pressed);
    await WaitForLayout(markets);
    Assert.True(markets.IsVisibleInTree());
    Assert.False(view.GetNode<Control>("Content").Visible);
    var rows = markets.GetNode<VBoxContainer>("%PredictionMarketRows").GetChildren();
    var inputs = rows.AsValueEnumerable().Select(row => row.GetNode<SpinBox>("%Stake")).ToArray();
    var selectors = rows.AsValueEnumerable().Select(row => row.GetNode<OptionButton>("%Currency")).ToArray();
    Assert.Equal(2, selectors.Length, "Each prediction must allow choosing an existing campaign currency.");
    Assert.True(inputs.AsValueEnumerable().All(input => input.Value == 0));
    Assert.False(SquadDeployButton(view).Disabled);
    inputs[0].Value = 101;
    Assert.Equal(100d, inputs[0].Value);
    inputs[0].Value = -1;
    Assert.Equal(0d, inputs[0].Value);
    inputs[0].Value = 100;
    inputs[1].Value = 200;
    foreach (string name in new[] { "MissionTitle", "PredictionMarketNotice", "PredictionMarketRows", "BackButton" })
      Assert.True(ScreenRect(view).Encloses(ScreenRect(markets.GetNode<Control>($"%{name}"))), name);
    foreach (Node row in rows)
      foreach (string name in new[] { "Description", "Currency", "Stake", "Return" })
        Assert.True(ScreenRect(view).Encloses(ScreenRect(row.GetNode<Control>($"%{name}"))), name);
    markets.BackButton!.EmitSignal(Button.SignalName.Pressed);
    Assert.True(view.GetNode<Control>("Content").Visible);
    Assert.True(ReferenceEquals(campaign.State.Roster[0], view.GetSelectedCombatants()[0]));
    view.GetNode<Button>("%PredictionMarketsButton").EmitSignal(Button.SignalName.Pressed);
    Assert.Equal(100d, inputs[0].Value);
    Assert.Equal(200d, inputs[1].Value);
    Assert.True(campaign.State.TrySpendCurrencyCosts(new() { [a] = 1 }));
    view.Present(campaign.State, campaign.Session);
    Assert.True(SquadDeployButton(view).Disabled);
    selectors[1].Select(1);
    selectors[1].EmitSignal(OptionButton.SignalName.ItemSelected, 1L);
    Assert.False(SquadDeployButton(view).Disabled);
    Assert.True(rows[0].GetNode<Label>("%Return").Text.Contains("Balance 299", StringComparison.Ordinal));
    Assert.True(rows[1].GetNode<Label>("%Return").Text.Contains("Balance 200", StringComparison.Ordinal));
    markets.BackButton!.EmitSignal(Button.SignalName.Pressed);
    view.GetNode<Button>("%PredictionMarketsButton").EmitSignal(Button.SignalName.Pressed);
    Assert.Equal(0, selectors[0].Selected);
    Assert.Equal(1, selectors[1].Selected);
    Assert.True(campaign.State.TrySpendCurrencyCosts(new() { [b] = 1 }));
    view.Present(campaign.State, campaign.Session);
    Assert.True(SquadDeployButton(view).Disabled);
    inputs[0].Value = 99;
    inputs[1].Value = 99;
    Assert.False(SquadDeployButton(view).Disabled);
    markets.BackButton!.EmitSignal(Button.SignalName.Pressed);
    SquadDeployButton(view).EmitSignal(Button.SignalName.Pressed);
    Assert.Equal(200UL, campaign.State.GetCurrencyBalance(a));
    Assert.Equal(100UL, campaign.State.GetCurrencyBalance(b));
    var host = view.GetNode<SubViewport>("Background/Viewport").GetChildren().AsValueEnumerable()
      .OfType<FunProject.Scenes.BattleScene>().Single();
    await DrainDirector(host);
    ReturnButton(host).EmitSignal(Button.SignalName.Pressed);
    var results = view.GetNode<MissionResultsView>("%MissionResultsView");
    await WaitForLayout(results);
    Assert.True(results.Visible);
    string text = results.GetNode<Label>("%AcceptedPredictions").Text;
    Assert.True(text.Contains("Correct", StringComparison.Ordinal));
    Assert.True(text.Contains("Incorrect", StringComparison.Ordinal));
    Assert.True(text.Contains("Stake 99 Credits; total return 148 Credits", StringComparison.Ordinal));
    Assert.True(text.Contains("Stake 99 Intel; total return 0 Intel", StringComparison.Ordinal));
    Assert.True(results.GetNode<Label>("%Rewards").Text.Contains("50", StringComparison.Ordinal));
    Assert.Equal(398UL, campaign.State.GetCurrencyBalance(a));
    Assert.Equal(100UL, campaign.State.GetCurrencyBalance(b));
    Assert.True(ScreenRect(results.GetNode<Control>("Panel/Margin/Center/Layout/Payments"))
      .Encloses(ScreenRect(results.GetNode<Label>("%AcceptedPredictions"))));
    Assert.True(ScreenRect(view).Encloses(ScreenRect(results.GetNode<Control>("%ContinueButton"))));
  }

  [TestCase]
  public static void GeneratedPredictionMarketsUseAuthoredTemplatesAndRemainStable()
  {
    var currency = MakeCurrency();
    var deployData = new DeployUnitPredictionMarketData { MaxStake = 17, Multiplier = 1.25 };
    var killsData = new UnitKillsPredictionMarketData
    {
      MaxStake = 23,
      Multiplier = 3.75,
      MinimumKills = 2,
      MaximumKills = 4,
    };
    using var campaign = Campaign(currency, roster: [MakeEntry("Alpha"), MakeEntry("Beta"), MakeEntry("Gamma")],
      configure: start =>
      {
        start.PredictionMarketPool.Clear();
        start.PredictionMarketPool.Add(deployData);
        start.PredictionMarketPool.Add(killsData);
      });
    var first = campaign.ActiveEvent.PredictionMarkets;
    var replay = campaign.State.GenerateMissionPredictionMarkets(campaign.ActiveEvent.BattleSeed.RequireSome());
    Assert.Equal(2, first.Count);
    for (int index = 0; index < first.Count; index++) Assert.Equal(first[index], replay[index]);
    Assert.Equal(17U, first[0].MaxStake);
    Assert.Equal(1.25m, first[0].Multiplier);
    Assert.Equal(23U, first[1].MaxStake);
    Assert.Equal(3.75m, first[1].Multiplier);
    uint retainedKills = ((UnitKillsPredictionMarket)first[1]).MinimumKills;
    var thresholds = ZLinq.ValueEnumerable.Range(0, 20)
      .Select(seed => ((UnitKillsPredictionMarket)campaign.State.GenerateMissionPredictionMarkets(seed)[1]).MinimumKills)
      .Distinct().ToArray();
    Assert.True(thresholds.Length > 1);
    Assert.True(thresholds.AsValueEnumerable().All(count => count >= 2 && count <= 4));
    deployData.MaxStake = 999;
    killsData.MinimumKills = killsData.MaximumKills = 99;
    var retained = new GeoscapeSession(campaign.State).PendingResolution.RequireSome().Event.PredictionMarkets;
    Assert.Equal(17U, retained[0].MaxStake);
    Assert.Equal(retainedKills, ((UnitKillsPredictionMarket)retained[1]).MinimumKills);
    using var noRewards = Campaign(currency, rewards: false);
    Assert.Equal(0, noRewards.ActiveEvent.Rewards.Count);
    Assert.Equal(2, noRewards.ActiveEvent.PredictionMarkets.Count);
    using var noFunds = Campaign(currency, balance: 0);
    Assert.Equal(2, noFunds.ActiveEvent.PredictionMarkets.Count);
    using var noRoster = Campaign(currency, roster: []);
    Assert.Equal(0, noRoster.ActiveEvent.PredictionMarkets.Count);
    using var noPool = Campaign(currency, configure: start => start.PredictionMarketPool.Clear());
    Assert.Equal(0, noPool.ActiveEvent.PredictionMarkets.Count);
    using var noWallet = Campaign(currency, configure: start => start.StartingCurrencies.Clear());
    Assert.Equal(2, noWallet.ActiveEvent.PredictionMarkets.Count);
    Assert.Equal(0, noWallet.State.Currencies.Count);
  }

  [TestCase]
  public static async Task EmptyWalletShowsMarketsWithoutAcceptingPredictions()
  {
    await using var cleanup = new DeferredNodeCleanup();
    using var campaign = Campaign(MakeCurrency(), configure: start => start.StartingCurrencies.Clear());
    var view = AddToTree(CreateMissionView());
    view.ConfigureMission(campaign.ActiveEvent);
    view.Present(campaign.State, campaign.Session);
    ChooseSquadUnit(view, 0, "Alpha");
    var rows = view.GetNode<VBoxContainer>("%PredictionMarketPreparationView/%PredictionMarketRows").GetChildren();
    Assert.Equal(2, rows.Count);
    foreach (Node row in rows)
    {
      Assert.True(row.GetNode<OptionButton>("%Currency").Disabled);
      Assert.False(row.GetNode<SpinBox>("%Stake").Editable);
      Assert.True(((PredictionMarketRow)row).SelectedPrediction.IsNone);
    }
    Assert.False(SquadDeployButton(view).Disabled);
  }

  [TestCase]
  public static async Task PresentationFailureLeavesStakesUnspent()
  {
    await using var cleanup = new DeferredNodeCleanup();
    var currency = MakeCurrency();
    using var campaign = Campaign(currency);
    var view = AddToTree(CreateMissionView());
    view.ConfigureMission(campaign.ActiveEvent);
    view.Present(campaign.State, campaign.Session);
    ChooseSquadUnit(view, 0, "Alpha");
    var prototype = CreateBattleScene();
    var camera = prototype.GetNode<Node>("GameCamera");
    prototype.RemoveChild(camera);
    camera.Free();
    view.BattleScene = Pack(prototype);
    var input = view.GetNode<VBoxContainer>("%PredictionMarketPreparationView/%PredictionMarketRows")
      .GetChild(0).GetNode<SpinBox>("%Stake");
    input.Value = 100;
    SquadDeployButton(view).EmitSignal(Button.SignalName.Pressed);
    Assert.Equal(1000UL, campaign.State.GetCurrencyBalance(currency));
    Assert.True(campaign.Session.ActiveMission.IsNone);
    Assert.True(campaign.Session.PendingResolution.IsSome);
    Assert.True(view.GetNode<Control>("Content").Visible);
  }

  [TestCase(0)]
  [TestCase(1)]
  public static void ConditionsExcludeUnitsSpawnedAfterDeployment(int marketIndex)
  {
    var currency = MakeCurrency();
    using var campaign = Campaign(currency, seed: 1, customize: mission => mission.Size.MaxPlayerUnits = 1,
      roster: [MakeEntry("Alpha"), MakeEntry("Beta")], configure: start =>
      {
        var kills = (UnitKillsPredictionMarketData)start.PredictionMarketPool[1];
        kills.MinimumKills = kills.MaximumKills = 1;
      });
    var market = campaign.ActiveEvent.PredictionMarkets[marketIndex];
    var target = market switch
    {
      DeployUnitPredictionMarket deploy => deploy.Unit,
      UnitKillsPredictionMarket kills => kills.Unit,
      _ => throw new InvalidOperationException("Unexpected authored market kind."),
    };
    var other = campaign.State.Roster.AsValueEnumerable().Single(unit => !ReferenceEquals(unit, target));
    var battle = campaign.Session.LaunchMission(campaign.ActiveEvent, [other, target]).RequireRight();
    using var runtime = battle.Runtime;
    Assert.True(campaign.Session.TryAcceptMissionPredictions([new(market, currency, 100)]));
    runtime.ExecuteAction(BattleAction.SpawnUnit(target, runtime.TryGetTile(new Vector3I(1, 0, 0)).RequireSome()));
    var summary = Win(battle, campaign, shooterX: 1);
    Assert.True(summary.CombatantsPresent.Contains(target));
    campaign.Session.CompleteMission(summary);
    Assert.False(battle.Deployment.Predictions[0].Correct);
    Assert.Equal(950UL, campaign.State.GetCurrencyBalance(currency));
  }
}
