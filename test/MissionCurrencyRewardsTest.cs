using System;
using System.Threading.Tasks;
using FunProject.Battle;
using FunProject.Currencies;
using FunProject.Geoscape;
using FunProject.Items.Effects;
using FunProject.Strategic;
using GdUnit4;
using Godot;
using static FunProject.Tests.GeoscapeTestScenes;

namespace FunProject.Tests;

[TestSuite]
[RequireGodotRuntime]
public class MissionCurrencyRewardsTest
{
  private static GeoscapeFixture Campaign(Currency currency, BattleOutcome outcome = BattleOutcome.Victory,
    Action<TacticalMissionData>? customize = null)
  {
    var mission = MakeTacticalMission(MakeTacticalBattleType(playerCells: 2, enemyCells: 2));
    mission.VictoryRewards.Add(new MissionCurrencyRewardData { Currency = currency, Minimum = 50, Maximum = 50 });
    mission.BattleType.Factions[0].Objectives.Clear();
    mission.BattleType.Factions[0].Objectives.Add(new EliminateAllOpposingForcesObjectiveData
    {
      OnComplete = new EndBattleDirectiveData { Outcome = outcome },
      OnFail = new EndBattleDirectiveData { Outcome = BattleOutcome.Defeat },
    });
    customize?.Invoke(mission);
    var campaign = new GeoscapeFixture(MakeStart(roster: [MakeEntry("Alpha")],
      startingCurrencies: new() { [currency] = 1000 },
      timeline: [MakeScheduled(1, MakeEvent("Reward mission", GeoscapeEventKind.TacticalBattle,
        tacticalMission: mission))]));
    campaign.AdvanceTicks(1);
    campaign.OpenResolution(campaign.ActiveEvent);
    return campaign;
  }

  private static FactionBattleSummary Finish(MissionBattle battle, GeoscapeFixture campaign)
  {
    var unit = battle.Runtime.Query(new GetUnitAtTile(battle.Runtime.TryGetTile(Vector3I.Zero).RequireSome())).RequireSome();
    var grenade = MakeGrenade("Frag", throwRange: 10, blastRadius: 0,
      effects: [new DamageEffectData { BaseDamage = 999 }]);
    unit.AddInventoryItem(grenade.Item);
    battle.Runtime.ExecuteAction(BattleAction.ThrowItem(
      battle.Runtime.TryGetAlive(unit).RequireSome(), grenade,
      battle.Runtime.TryGetTile(new Vector3I(2, 0, 0)).RequireSome()));
    return battle.Runtime.Query(new GetFactionEndOfBattleSummary(campaign.State.PlayerFaction)).RequireRight();
  }

  [TestCase(BattleOutcome.Victory, 1050UL)]
  [TestCase(BattleOutcome.Defeat, 1000UL)]
  [TestCase(BattleOutcome.Draw, 1000UL)]
  public static void OnlyVictoryPaysAndSettlementCommitsBeforeNotifications(BattleOutcome outcome, ulong expected)
  {
    var currency = MakeCurrency();
    using var campaign = Campaign(currency, outcome);
    var battle = campaign.Session.LaunchMission(campaign.ActiveEvent, campaign.State.Roster).RequireRight();
    using var runtime = battle.Runtime;
    campaign.Session.ConfirmMissionDeployment(battle.Deployment);
    var summary = Finish(battle, campaign);
    var result = runtime.Query(new GetBattleResultQuery()).RequireRight();
    var rebuilt = new GeoscapeSession(campaign.State);
    int notifications = 0;
    rebuilt.EventCommitted += _ =>
    {
      notifications++;
      Assert.Equal(expected, campaign.State.GetCurrencyBalance(currency));
      Assert.True(rebuilt.ActiveMission.IsNone);
      Assert.True(rebuilt.PendingResolution.IsNone);
      rebuilt.CompleteMission(battle.Deployment, summary, result);
    };
    var receipt = rebuilt.CompleteMission(battle.Deployment, summary, result);
    runtime.Dispose();
    Assert.Equal(expected, campaign.State.GetCurrencyBalance(currency));
    Assert.Equal(outcome, receipt.Outcome.RequireSome());
    Assert.Equal(1, receipt.Tally.RequireSome().EnemiesKilled);
    Assert.Equal(2, notifications);
    Assert.True(ReferenceEquals(receipt, new GeoscapeSession(campaign.State).LastMissionReceipt.RequireSome()));
    Assert.True(ReferenceEquals(receipt, rebuilt.CompleteMission(battle.Deployment, summary, result)));
  }

  [TestCase]
  public static void RewardRollsRetainCurrencyIdentityAndStayStableAcrossReopening()
  {
    var currency = MakeCurrency();
    var sameName = MakeCurrency();
    using var campaign = Campaign(currency, customize: mission =>
    {
      mission.VictoryRewards[0].Minimum = 10;
      mission.VictoryRewards[0].Maximum = 100;
      mission.VictoryRewards.Add(new MissionCurrencyRewardData { Currency = currency, Minimum = 2, Maximum = 2 });
      mission.VictoryRewards.Add(new MissionCurrencyRewardData { Currency = sameName, Minimum = 3, Maximum = 3 });
    });
    var mission = campaign.ActiveEvent;
    var first = GeoscapeSession.GetMissionTerms(mission);
    Assert.Equal(2, first.VictoryRewards.Count);
    Assert.True(first.VictoryRewards[currency] >= 12 && first.VictoryRewards[currency] <= 102);
    Assert.Equal(3UL, first.VictoryRewards[sameName]);
    Assert.True(ReferenceEquals(first, GeoscapeSession.GetMissionTerms(mission)));
    mission.Definition.TacticalMission!.VictoryRewards.Clear();
    Assert.True(ReferenceEquals(first, GeoscapeSession.GetMissionTerms(mission)));
    Assert.Equal(0, GeoscapeSession.GetMissionTerms(mission with { Terms = null }).VictoryRewards.Count);
  }

  [TestCase]
  public static void MalformedRewardRangesRejectBeforePayment()
  {
    var currency = MakeCurrency();
    using var campaign = Campaign(currency, customize: mission => mission.VictoryRewards[0].Minimum = 51);
    Assert.Throws<InvalidOperationException>(() => GeoscapeSession.GetMissionTerms(campaign.ActiveEvent));
    Assert.Equal(1000UL, campaign.State.GetCurrencyBalance(currency));
  }

  [TestCase(false)]
  [TestCase(true)]
  public static void FailedSettlementDoesNotPartiallyMutateOrPayAgain(bool conditionOverflow)
  {
    var currency = MakeCurrency();
    var full = MakeCurrency("Full");
    using var campaign = Campaign(currency, customize: mission =>
      mission.VictoryRewards.Add(new MissionCurrencyRewardData { Currency = full, Minimum = 1, Maximum = 1 }));
    campaign.State.CreditCurrencies(new SysColGeneric.Dictionary<Currency, ulong> { [full] = ulong.MaxValue });
    var battle = campaign.Session.LaunchMission(campaign.ActiveEvent, campaign.State.Roster).RequireRight();
    using var runtime = battle.Runtime;
    campaign.Session.ConfirmMissionDeployment(battle.Deployment);
    var summary = Finish(battle, campaign);
    var result = runtime.Query(new GetBattleResultQuery()).RequireRight();
    if (conditionOverflow)
    {
      Assert.True(campaign.State.TrySpendCurrencyCosts(new() { [full] = 1 }));
      campaign.State.Tick = long.MaxValue;
    }
    Assert.Throws<OverflowException>(() => campaign.Session.CompleteMission(battle.Deployment, summary, result));
    Assert.Equal(1000UL, campaign.State.GetCurrencyBalance(currency));
    Assert.True(battle.Deployment.Receipt.IsNone);
    Assert.True(campaign.Session.ActiveMission.IsSome);
    Assert.True(campaign.State.Conditions.GetFatigue(campaign.State.Roster[0]).IsNone);
    campaign.State.Tick = 1;
    if (!conditionOverflow) Assert.True(campaign.State.TrySpendCurrencyCosts(new() { [full] = 1 }));
    campaign.Session.CompleteMission(battle.Deployment, summary, result);
    Assert.Equal(1050UL, campaign.State.GetCurrencyBalance(currency));
    Assert.Equal(ulong.MaxValue, campaign.State.GetCurrencyBalance(full));
    Assert.Equal(1, campaign.State.Conditions.GetFatigue(campaign.State.Roster[0]).RequireSome().Tier);
  }

  [TestCase(800, 600)]
  [TestCase(1600, 900)]
  public static async Task RewardResultsReopenAfterSubscriberFailureWithoutRepayment(int width, int height)
  {
    await using var cleanup = new DeferredNodeCleanup();
    var currency = MakeCurrency();
    using var campaign = Campaign(currency);
    var battle = campaign.Session.LaunchMission(campaign.ActiveEvent, campaign.State.Roster).RequireRight();
    using var runtime = battle.Runtime;
    campaign.Session.ConfirmMissionDeployment(battle.Deployment);
    var summary = Finish(battle, campaign);
    var result = runtime.Query(new GetBattleResultQuery()).RequireRight();
    campaign.Session.EventCommitted += _ => throw new InvalidOperationException("Notification failed");
    Assert.Throws<InvalidOperationException>(() => campaign.Session.CompleteMission(battle.Deployment, summary, result));
    runtime.Dispose();
    var view = GD.Load<PackedScene>("res://scenes/geoscape/results/MissionResultsView.tscn").Instantiate<MissionResultsView>();
    CreateScaledWindow(view, new Vector2I(width, height));
    view.Present(campaign.State, new GeoscapeSession(campaign.State));
    await WaitForLayout(view);
    Assert.True(view.GetNode<Label>("%Rewards").Text.Contains("50", StringComparison.Ordinal));
    Assert.Equal("1", view.GetNode<Label>("%EnemiesKilled").Text);
    Assert.Equal(1050UL, campaign.State.GetCurrencyBalance(currency));
    Assert.True(ScreenRect(view).Encloses(ScreenRect(view.GetNode<Control>("%Rewards"))));
    Assert.True(ScreenRect(view).Encloses(ScreenRect(view.GetNode<Control>("%ContinueButton"))));
    Assert.True(ReferenceEquals(battle.Deployment.Receipt.RequireSome(),
      campaign.Session.CompleteMission(battle.Deployment, summary, result)));
  }

  [TestCase]
  public static async Task AbandonmentRetainsResultsWithoutGrantingRewards()
  {
    await using var cleanup = new DeferredNodeCleanup();
    var currency = MakeCurrency();
    using var campaign = Campaign(currency);
    var view = AddToTree(CreateMissionView());
    view.ConfigureMission(campaign.ActiveEvent);
    view.Present(campaign.State, campaign.Session);
    ChooseSquadUnit(view, 0, "Alpha");
    Assert.True(view.GetNode<Label>("%RewardPreview").Text.Contains("50", StringComparison.Ordinal));
    SquadDeployButton(view).EmitSignal(Button.SignalName.Pressed);
    view.GetNode<Button>("%AbandonButton").EmitSignal(Button.SignalName.Pressed);
    var receipt = campaign.Session.LastMissionReceipt.RequireSome();
    Assert.True(receipt.Outcome.IsNone);
    Assert.Equal(0, receipt.Rewards.Count);
    Assert.Equal(1000UL, campaign.State.GetCurrencyBalance(currency));
    Assert.True(view.GetNode<MissionResultsView>("%MissionResultsView").Visible);
    var reopened = AddToTree(GD.Load<PackedScene>("res://scenes/geoscape/results/MissionResultsView.tscn").Instantiate<MissionResultsView>());
    reopened.Present(campaign.State, new GeoscapeSession(campaign.State));
    Assert.Equal("—", reopened.GetNode<Label>("%TurnsTaken").Text);
  }
}
