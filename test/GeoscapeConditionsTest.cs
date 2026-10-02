using FunProject.Battle;
using FunProject.Combatants;
using FunProject.Strategic;
using GdUnit4;

// Campaign condition integration: mission returns applied to roster combatants, deployment
// eligibility, and timed recovery on geoscape ticks. Battles use the campaign's own roster
// instances so returns write the persistent state the next mission will read. Assertions
// use integer tier positions (0 = Healthy/Fresh); the default ladders run 0..4 injury and
// 0..3 fatigue.
[TestSuite]
[RequireGodotRuntime]
public class GeoscapeConditionsTest
{
  private static GeoscapeFixture NewCampaign(params (string Name, int Health)[] roster)
    => new(TestData.MakeStart(roster:
      [.. roster.AsValueEnumerable().Select(entry =>
        TestData.MakeEntry(entry.Name, TestData.MakeCombatantData(health: entry.Health)))]));

  // 0 marks an absent injury record; the unconscious row targets the 20-health second
  // roster member of a two-member campaign.
  [TestCase(BattleOutcome.Victory, false, 25, 0, 2)]
  [TestCase(BattleOutcome.Victory, false, 0, 30, 0)]
  [TestCase(BattleOutcome.Defeat, false, 25, 0, 2)]
  [TestCase(BattleOutcome.Victory, true, 0, 30, 0)]
  public void SurvivorMissionReturnsApplyConditions(
    BattleOutcome outcome, bool unconsciousCase, int damage, int stun, int expectedInjuryTier)
  {
    using var campaign = unconsciousCase
      ? NewCampaign(("Alpha", 100), ("Beta", 20))
      : NewCampaign(("Alpha", 100));
    var target = campaign.State.Roster[unconsciousCase ? 1 : 0];

    campaign.ClearEvents();
    campaign.Session.ApplyMissionReturn(campaign.PlayMission(outcome, (target, damage, stun)));

    if (expectedInjuryTier == 0)
      Assert.True(campaign.State.Conditions.GetInjury(target).IsNone);
    else
    {
      Assert.Equal(expectedInjuryTier, campaign.State.Conditions.GetInjury(target).RequireSome().Tier); // Injured
      Assert.Equal(8640L, campaign.State.Conditions.GetInjury(target).RequireSome().RecoveryTick);
    }
    Assert.Equal(1, campaign.State.Conditions.GetFatigue(target).RequireSome().Tier); // Tired
    Assert.Equal(1440L, campaign.State.Conditions.GetFatigue(target).RequireSome().RecoveryTick);
    Assert.Equal(0, target.Rank.Xp, "Returns never award experience.");
    var events = campaign.Events.EventsOf<CombatantConditionsChanged>();
    Assert.Equal(1, events.Length);
    Assert.True(ReferenceEquals(target, events[0].Combatant));
  }

  [TestCase(TestName = "Armor-absorbed returns earn fatigue but no injury")]
  public void ArmorOnlyReturnsEarnFatigueWithoutInjury()
  {
    using var campaign = NewCampaign(("Alpha", 100));
    var alpha = campaign.State.Roster[0];
    using var battle = new BattleFixture(new Vector3I(5, 1, 5), [campaign.State.PlayerFaction]);
    var unit = battle.Spawn(alpha, Vector3I.Zero, armor: TestData.MakeArmor("Vest", armor: 10));
    battle.AddObjective(campaign.State.PlayerFaction, new SurviveUntilTurnObjectiveData
    {
      TargetTurn = 2,
      OnComplete = new EndBattleDirectiveData { Outcome = BattleOutcome.Victory },
    }.Instantiate());
    battle.Start();
    battle.ApplyDamage(unit, 5);
    battle.EndFactionTurn(campaign.State.PlayerFaction);   // turn two's start completes
    var summary = battle.Query(new GetCompletedBattleQuery()).RequireSome().FactionSummaries[campaign.State.PlayerFaction];
    Assert.Equal(0L, summary.HealthByCombatant[alpha].HealthDamageTaken);

    campaign.Session.ApplyMissionReturn(summary);

    Assert.True(campaign.State.Conditions.GetInjury(alpha).IsNone);
    Assert.Equal(1, campaign.State.Conditions.GetFatigue(alpha).RequireSome().Tier);
  }

  [TestCase(TestName = "Dead participants are skipped, overkill or not")]
  public void DeadParticipantsAreSkipped()
  {
    using var campaign = NewCampaign(("Alpha", 100), ("Beta", 20), ("Charlie", 100));
    var alpha = campaign.State.Roster[0];
    var beta = campaign.State.Roster[1];
    var charlie = campaign.State.Roster[2];

    campaign.ClearEvents();
    campaign.ReturnFromMission((alpha, 25, 0), (beta, 999, 0));

    Assert.Equal(2, campaign.State.Conditions.GetInjury(alpha).RequireSome().Tier);
    Assert.True(campaign.State.Conditions.GetInjury(beta).IsNone);
    Assert.True(campaign.State.Conditions.GetFatigue(beta).IsNone);
    Assert.True(campaign.State.Conditions.GetInjury(charlie).IsNone);
    Assert.True(campaign.State.Conditions.GetFatigue(charlie).IsNone);
    var events = campaign.Events.EventsOf<CombatantConditionsChanged>();
    Assert.Equal(1, events.Length);
    Assert.True(ReferenceEquals(alpha, events[0].Combatant));
    Assert.True(campaign.Events.EventsOf<CombatantConditionsChanged>()
      .AsValueEnumerable().All(changed => !ReferenceEquals(charlie, changed.Combatant)));
  }

  [TestCase(TestName = "All survivor states are applied before the first change is broadcast")]
  public void AllSurvivorStatesAppliedBeforeFirstBroadcast()
  {
    using var campaign = NewCampaign(("Alpha", 100), ("Beta", 100));
    var alpha = campaign.State.Roster[0];
    var beta = campaign.State.Roster[1];
    int broadcasts = 0;
    campaign.Session.EventCommitted += evt =>
    {
      if (evt is not CombatantConditionsChanged)
        return;
      broadcasts++;
      if (broadcasts == 1)
      {
        Assert.Equal(2, campaign.State.Conditions.GetInjury(alpha).RequireSome().Tier);
        Assert.Equal(2, campaign.State.Conditions.GetInjury(beta).RequireSome().Tier);
        Assert.Equal(1, campaign.State.Conditions.GetFatigue(alpha).RequireSome().Tier);
        Assert.Equal(1, campaign.State.Conditions.GetFatigue(beta).RequireSome().Tier);
      }
    };

    campaign.ReturnFromMission((alpha, 25, 0), (beta, 30, 0));

    Assert.Equal(2, broadcasts);
  }

  [TestCase(TestName = "Applying the same report twice steps conditions each time")]
  public void RepeatedReturnsApplyEachTime()
  {
    using var campaign = NewCampaign(("Alpha", 100));
    var alpha = campaign.State.Roster[0];
    FactionBattleSummary report = campaign.PlayMission(squad: [(alpha, 10, 0)]);

    campaign.ClearEvents();
    campaign.Session.ApplyMissionReturn(report);
    Assert.Equal(1, campaign.State.Conditions.GetInjury(alpha).RequireSome().Tier); // Lightly Injured
    Assert.Equal(1, campaign.State.Conditions.GetFatigue(alpha).RequireSome().Tier); // Tired
    Assert.Equal(1, campaign.Events.EventsOf<CombatantConditionsChanged>().Length);

    campaign.Session.ApplyMissionReturn(report);
    Assert.Equal(2, campaign.State.Conditions.GetInjury(alpha).RequireSome().Tier); // Injured
    Assert.Equal(1, campaign.State.Conditions.GetFatigue(alpha).RequireSome().Tier); // Tired — already injured
    Assert.Equal(2, campaign.Events.EventsOf<CombatantConditionsChanged>().Length);
  }

  [TestCase(TestName = "Zero-participant battles apply no changes and fire no events")]
  public void ZeroParticipantBattlesApplyNoChanges()
  {
    using var campaign = NewCampaign(("Alpha", 100));
    var alpha = campaign.State.Roster[0];
    // Zero participants: no legal battle exists (preparation requires units), so the
    // report arrangement is the frozen empty summary the battle pipeline would emit.
    var empty = GeoscapeFixture.EmptyReport(campaign.State.PlayerFaction, BattleOutcome.Victory);

    campaign.ClearEvents();
    campaign.Session.ApplyMissionReturn(empty);

    Assert.Equal(0, campaign.Events.EventsOf<CombatantConditionsChanged>().Length);
    Assert.True(campaign.State.Conditions.GetFatigue(alpha).IsNone);
  }

  // The fatigue row (three real zero-damage returns) and the injury row (one 25% return)
  // independently check the session's mission-flag plumbing.
  [TestCase(25, 1, 1)]
  [TestCase(0, 3, 3)]
  public void MissionDeploymentRejectsUnfitCombatants(int damage, int returns, int expectedFatigueTier)
  {
    using var campaign = NewCampaign(("Alpha", 100));
    var alpha = campaign.State.Roster[0];
    var mission = TestData.MakeEvent("Patrol");

    Assert.True(campaign.Session.CanDeploy(alpha, mission));
    for (int i = 0; i < returns; i++)
      campaign.ReturnFromMission((alpha, damage, 0));
    Assert.Equal(expectedFatigueTier, campaign.State.Conditions.GetFatigue(alpha).RequireSome().Tier);
    Assert.False(campaign.Session.CanDeploy(alpha, mission),
      "Unfit combatants cannot deploy on an ordinary mission.");

    var lastStand = TestData.MakeEvent("Last Stand", allowUnfitDeployment: true);
    Assert.True(campaign.Session.CanDeploy(alpha, lastStand));
  }

  [TestCase(TestName = "Recovery lands exactly on the deadline, not before")]
  public void RecoveryLandsExactlyOnDeadline()
  {
    using var campaign = NewCampaign(("Alpha", 100));
    var alpha = campaign.State.Roster[0];
    campaign.ReturnFromMission((alpha, 25, 0));

    campaign.ClearEvents();
    campaign.AdvanceTicks(1439);
    Assert.Equal(1, campaign.State.Conditions.GetFatigue(alpha).RequireSome().Tier);
    Assert.Equal(0, campaign.Events.EventsOf<CombatantConditionsChanged>().Length);

    campaign.AdvanceTicks(1);
    Assert.True(campaign.State.Conditions.GetFatigue(alpha).IsNone);
    Assert.Equal(2, campaign.State.Conditions.GetInjury(alpha).RequireSome().Tier, "The injury deadline is still ahead.");
    Assert.Equal(1, campaign.Events.EventsOf<CombatantConditionsChanged>().Length);
  }

  [TestCase(TestName = "Paused time never recovers conditions")]
  public void PausedTimeNeverRecovers()
  {
    using var campaign = NewCampaign(("Alpha", 100));
    var alpha = campaign.State.Roster[0];
    campaign.ReturnFromMission((alpha, 25, 0));

    campaign.ClearEvents();
    campaign.ChangeSpeed(TimeSpeed.Paused);
    campaign.Advance(10000.0);

    Assert.Equal(1, campaign.State.Conditions.GetFatigue(alpha).RequireSome().Tier);
    Assert.Equal(0, campaign.Events.EventsOf<CombatantConditionsChanged>().Length);
  }

  [TestCase(TestName = "Pending resolutions freeze recovery")]
  public void PendingResolutionsFreezeRecovery()
  {
    using var campaign = NewCampaign(("Alpha", 100));
    var alpha = campaign.State.Roster[0];
    campaign.ReturnFromMission((alpha, 25, 0));

    campaign.ClearEvents();
    campaign.Session.OpenResolution(new GeoscapeEvent(TestData.MakeEvent("Crisis"), None, 0, None));
    campaign.AdvanceTicks(2000);

    Assert.Equal(1, campaign.State.Conditions.GetFatigue(alpha).RequireSome().Tier);
    Assert.Equal(0, campaign.Events.EventsOf<CombatantConditionsChanged>().Length);
  }

  [TestCase(TestName = "Multi-day catch-up walks the whole injury ladder in one advance")]
  public void MultiDayCatchUpWalksTheLadder()
  {
    using var campaign = NewCampaign(("Alpha", 100));
    var alpha = campaign.State.Roster[0];
    campaign.ReturnFromMission((alpha, 60, 0));
    Assert.Equal(3, campaign.State.Conditions.GetInjury(alpha).RequireSome().Tier); // Gravely Injured

    campaign.ClearEvents();
    campaign.AdvanceTicks(37440);

    Assert.True(campaign.State.Conditions.GetInjury(alpha).IsNone);
    Assert.True(campaign.State.Conditions.GetFatigue(alpha).IsNone);
    // Three injury transitions (Gravely -> Injured -> Lightly -> Healthy) plus the one
    // fatigue step to Fresh.
    Assert.Equal(4, campaign.Events.EventsOf<CombatantConditionsChanged>().Length);
  }

  [TestCase(TestName = "Rebuilt sessions continue recovery from campaign state")]
  public void RebuiltSessionsPreserveRecovery()
  {
    using var campaign = NewCampaign(("Alpha", 100));
    var alpha = campaign.State.Roster[0];
    campaign.ReturnFromMission((alpha, 25, 0));
    campaign.AdvanceTicks(1440);
    Assert.True(campaign.State.Conditions.GetFatigue(alpha).IsNone);

    var rebuilt = new GeoscapeSession(campaign.State);
    rebuilt.ChangeSpeed(TimeSpeed.Normal);
    rebuilt.Advance((8640 - 1440) * 0.1);

    Assert.Equal(1, campaign.State.Conditions.GetInjury(alpha).RequireSome().Tier); // Lightly Injured
  }

  [TestCase(TestName = "Roster recovery runs last in the preserved tick order")]
  public void TickOrderPutsRecoveryAfterResearch()
  {
    using var campaign = new GeoscapeFixture(TestData.MakeStart(
      regions: [TestData.MakeRegion("North")],
      timeline: [TestData.MakeScheduled(23040, TestData.MakeEvent("Signal"))],
      roster: [TestData.MakeEntry("Alpha", TestData.MakeCombatantData(health: 100))],
      researchProjects: [TestData.MakeResearch(days: 16)]));
    var alpha = campaign.State.Roster[0];
    campaign.ReturnFromMission((alpha, 60, 0));
    campaign.Session.StartResearch(campaign.Session.GetResearchProjects()[0]);
    campaign.AdvanceTicks(2160); // the fatigue ladder finishes; the injury deadline stays ahead

    campaign.ClearEvents();
    campaign.AdvanceTicks(20880); // lands exactly on the 23040 injury deadline, shared with
    // the scheduled signal and the sixteen-day research project

    var events = campaign.Events;
    int time = events.EventIndex<TimeAdvanced>(advanced => advanced.Tick == 23040);
    Assert.True(time >= 0, "Expected a TimeAdvanced event for the deadline tick.");
    Assert.True(time < events.EventIndex<ScheduledEventFired>(), "Time advances first.");
    Assert.True(events.EventIndex<ScheduledEventFired>() < events.EventIndex<ResearchCompleted>(),
      "Scheduled events fire before research completion.");
    Assert.True(events.EventIndex<ResearchCompleted>() < events.EventIndex<CombatantConditionsChanged>(),
      "Roster recovery runs after research completion.");
    Assert.Equal(2, campaign.State.Conditions.GetInjury(alpha).RequireSome().Tier); // Injured
  }

  [TestCase(TestName = "Critical injury and exhaustion recover independently down both ladders")]
  public void CriticalAndExhaustedRecoverIndependently()
  {
    using var campaign = NewCampaign(("Alpha", 100));
    var alpha = campaign.State.Roster[0];
    // Fatigue is built before the injury: returns while injured add no fatigue.
    campaign.ReturnFromMission((alpha, 0, 0));
    campaign.ReturnFromMission((alpha, 0, 0));
    campaign.ReturnFromMission((alpha, 75, 0));
    Assert.Equal(4, campaign.State.Conditions.GetInjury(alpha).RequireSome().Tier); // Critically Injured
    Assert.Equal(3, campaign.State.Conditions.GetFatigue(alpha).RequireSome().Tier); // Exhausted
    Assert.False(campaign.Session.CanDeploy(alpha, TestData.MakeEvent("Patrol")),
      "Critical + Exhausted blocks deployment.");
    Assert.True(campaign.Session.CanDeploy(
      alpha, TestData.MakeEvent("Last Stand", allowUnfitDeployment: true)));

    campaign.ClearEvents();
    campaign.AdvanceTicks(11520);
    Assert.Equal(2, campaign.State.Conditions.GetFatigue(alpha).RequireSome().Tier); // Weary
    Assert.Equal(4, campaign.State.Conditions.GetInjury(alpha).RequireSome().Tier);
    campaign.AdvanceTicks(5760);
    Assert.Equal(1, campaign.State.Conditions.GetFatigue(alpha).RequireSome().Tier); // Tired
    campaign.AdvanceTicks(1440);
    Assert.True(campaign.State.Conditions.GetFatigue(alpha).IsNone); // Fresh
    campaign.AdvanceTicks(24480);
    Assert.Equal(3, campaign.State.Conditions.GetInjury(alpha).RequireSome().Tier); // Gravely Injured
    campaign.AdvanceTicks(23040);
    Assert.Equal(2, campaign.State.Conditions.GetInjury(alpha).RequireSome().Tier); // Injured
    campaign.AdvanceTicks(8640);
    Assert.Equal(1, campaign.State.Conditions.GetInjury(alpha).RequireSome().Tier); // Lightly Injured
    campaign.AdvanceTicks(5760);

    Assert.True(campaign.State.Conditions.GetInjury(alpha).IsNone);
    // Fatigue steps down three times (Exhausted -> Fresh), injury four times
    // (Critical -> Healthy).
    Assert.Equal(7, campaign.Events.EventsOf<CombatantConditionsChanged>().Length);
  }
}
