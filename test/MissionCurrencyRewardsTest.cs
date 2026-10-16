using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FunProject.Battle;
using FunProject.Currencies;
using FunProject.GameState;
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
  private static MissionCurrencyRewardData Entry(Currency currency, uint minimum = 50, uint maximum = 50)
    => new() { Currency = currency, Minimum = minimum, Maximum = maximum };

  private static GeoscapeFixture Campaign(Currency currency, BattleOutcome outcome = BattleOutcome.Victory)
  {
    var mission = MakeTacticalMission(MakeTacticalBattleType(playerCells: 2, enemyCells: 2));
    mission.BattleType.Factions[0].Objectives.Clear();
    mission.BattleType.Factions[0].Objectives.Add(new EliminateAllOpposingForcesObjectiveData
    {
      OnComplete = new EndBattleDirectiveData { Outcome = outcome },
      OnFail = new EndBattleDirectiveData { Outcome = BattleOutcome.Defeat },
    });
    var start = MakeStart(roster: [MakeEntry("Alpha")], startingCurrencies: new() { [currency] = 1000 },
      timeline: [MakeScheduled(1, MakeEvent("Reward mission", GeoscapeEventKind.TacticalBattle, tacticalMission: mission))]);
    start.MissionRewardPool.Add(Entry(currency));
    var campaign = GeoscapeFixture.WithFiredEvent(start);
    campaign.OpenResolution(campaign.ActiveEvent);
    return campaign;
  }

  private static FactionBattleSummary Finish(MissionBattle battle, GeoscapeFixture campaign)
  {
    var unit = battle.Runtime.Query(new GetUnitAtTile(battle.Runtime.TryGetTile(Vector3I.Zero).RequireSome())).RequireSome();
    var grenade = MakeGrenade("Frag", throwRange: 10, blastRadius: 0,
      effects: [new DamageEffectData { BaseDamage = 999 }]);
    unit.AddInventoryItem(grenade.Item);
    battle.Runtime.ExecuteAction(BattleAction.ThrowItem(battle.Runtime.TryGetAlive(unit).RequireSome(), grenade,
      battle.Runtime.TryGetTile(new Vector3I(2, 0, 0)).RequireSome()));
    return battle.Runtime.Query(new GetFactionEndOfBattleSummary(campaign.State.PlayerFaction)).RequireRight();
  }

  [TestCase]
  public static void GeneratedSubsetsUseAuthoredRangesCurrencyIdentityAndSeparateSeeds()
  {
    var a = MakeCurrency("Same");
    var b = MakeCurrency("Same");
    var c = MakeCurrency("Other");
    MissionCurrencyRewardData[] pool = [Entry(a, 10, 20), Entry(b, 30, 40), Entry(c, 50, 60)];
    var start = MakeStart();
    foreach (var entry in pool) start.MissionRewardPool.Add(entry);
    using var campaign = new GeoscapeFixture(start);
    var sets = new SysColGeneric.HashSet<string>(StringComparer.Ordinal);
    for (int seed = 0; seed < 24; seed++)
    {
      var rewards = campaign.State.GenerateMissionRewards(seed);
      var replay = campaign.State.GenerateMissionRewards(seed);
      Assert.True(rewards.Count >= 1 && rewards.Count <= 3);
      foreach (var (currency, amount) in rewards)
      {
        var entry = pool.AsValueEnumerable().Single(x => ReferenceEquals(x.Currency, currency));
        Assert.True(amount >= entry.Minimum && amount <= entry.Maximum);
        Assert.Equal(amount, replay[currency]);
      }
      Assert.Equal(rewards.Count, replay.Count);
      sets.Add(string.Join(",", pool.AsValueEnumerable().Select(x => rewards.ContainsKey(x.Currency) ? "1" : "0").ToArray()));
    }
    Assert.True(sets.Count > 1, "Different event seeds must vary the reward subset.");
    using var emptyCampaign = new GeoscapeFixture(MakeStart());
    Assert.Equal(0, emptyCampaign.State.GenerateMissionRewards(1).Count);
  }

  [TestCase(0U, 1U)]
  [TestCase(uint.MaxValue - 1, uint.MaxValue)]
  [TestCase(uint.MaxValue, uint.MaxValue)]
  public static void RewardAmountBoundsAreInclusive(uint minimum, uint maximum)
  {
    var currency = MakeCurrency();
    var start = MakeStart();
    start.MissionRewardPool.Add(Entry(currency, minimum, maximum));
    using var campaign = new GeoscapeFixture(start);
    var amounts = new SysColGeneric.HashSet<ulong>();
    for (int seed = 0; seed < 24; seed++)
      amounts.Add(campaign.State.GenerateMissionRewards(seed)[currency]);
    Assert.True(amounts.SetEquals(new ulong[] { minimum, maximum }));
  }

  [TestCase(800, 600)]
  [TestCase(1600, 900)]
  public static async Task NotificationPreviewsStableEventRewardsAndSquadHasNoPreview(int width, int height)
  {
    await using var cleanup = new DeferredNodeCleanup();
    var currency = MakeCurrency("Credits");
    using var campaign = Campaign(currency);
    var mission = campaign.ActiveEvent;
    var dialog = CreateResolutionView();
    CreateScaledWindow(dialog, new Vector2I(width, height));
    dialog.Present(campaign.State, campaign.Session);
    var rebuilt = new GeoscapeSession(campaign.State);
    dialog.Present(campaign.State, rebuilt);
    await WaitForLayout(dialog);
    Assert.True(dialog.GetNode<Label>("%Description").Text.Contains("50 Credits", StringComparison.Ordinal));
    Assert.True(ReferenceEquals(mission.Rewards, rebuilt.ActiveEvents[0].Rewards));
    Assert.Equal(1000UL, campaign.State.GetCurrencyBalance(currency));
    var squad = AddToTree(CreateMissionView());
    squad.ConfigureMission(mission);
    squad.Present(campaign.State, rebuilt);
    Assert.True(squad.GetNodeOrNull<Control>("%RewardPreview") is null);
    Assert.True(ScreenRect(dialog).Encloses(ScreenRect(dialog.GetNode<Control>("%Buttons"))));
  }

  [TestCase(BattleOutcome.Victory, 1050UL)]
  [TestCase(BattleOutcome.Defeat, 1000UL)]
  [TestCase(BattleOutcome.Draw, 1000UL)]
  public static void OnlyVictoryCreditsBeforeNotifications(BattleOutcome outcome, ulong expected)
  {
    var currency = MakeCurrency();
    using var campaign = Campaign(currency, outcome);
    var battle = campaign.Session.LaunchMission(campaign.ActiveEvent, campaign.State.Roster).RequireRight();
    using var runtime = battle.Runtime;
    var summary = Finish(battle, campaign);
    campaign.Session.EventCommitted += _ =>
    {
      Assert.Equal(expected, campaign.State.GetCurrencyBalance(currency));
      Assert.True(campaign.Session.PendingResolution.IsNone);
      throw new InvalidOperationException("Notification failed");
    };
    Assert.Throws<InvalidOperationException>(() => campaign.Session.CompleteMission(summary));
    Assert.Equal(expected, campaign.State.GetCurrencyBalance(currency));
  }

  [TestCase]
  public static void PoolIsIndependentOfStartingWallet()
  {
    var wallet = MakeCurrency("Wallet only");
    var reward = MakeCurrency("Reward only");
    var start = MakeStart(startingCurrencies: new() { [wallet] = 1000 },
      timeline: [MakeScheduled(1, MakeEvent("Reward", GeoscapeEventKind.TacticalBattle))]);
    start.MissionRewardPool.Add(Entry(reward));
    using var campaign = new GeoscapeFixture(start);
    campaign.AdvanceTicks(1);
    Assert.Equal(50UL, campaign.ActiveEvent.Rewards[reward]);
    Assert.False(campaign.ActiveEvent.Rewards.ContainsKey(wallet));
  }
}
