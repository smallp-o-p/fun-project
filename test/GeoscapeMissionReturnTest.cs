using System;
using System.Collections.Generic;
using CampaignGameState = global::FunProject.GameState.GameState;
using FunProject.Battle;
using FunProject.Combatants;
using FunProject.Strategic;
using FunProject.Weapons;
using GdUnit4;

namespace FunProject.Tests;

// Campaign mission-return boundary: CompleteMission applies one terminal battle return
// exactly once — survivor conditions, roster removal of reported deaths, victory-only
// captures, and consumption of the mission, its pending resolution, and the association —
// before the first notification. Battles run on real launched runtimes; tests drive
// damage/stun through the handle's runtime with fresh proof mints and dispose the
// host-owned runtime themselves.
[TestSuite]
[RequireGodotRuntime]
public class GeoscapeMissionReturnTest
{
  // Three player cells support a mixed squad; Delta stays outside the mission.
  // The player's elimination objective ends the real battle on victory or defeat.
  private static GeoscapeFixture NewMissionCampaign()
  {
    TacticalMissionData mission = MakeTacticalMission(
      MakeTacticalBattleType(playerCells: 3, enemyCells: 2),
      maxPlayerUnits: 3, minEnemyUnits: 1, maxEnemyUnits: 1);
    mission.BattleType.Factions[0].Objectives.Clear();
    mission.BattleType.Factions[0].Objectives.Add(new EliminateAllOpposingForcesObjectiveData
    {
      OnComplete = new EndBattleDirectiveData { Outcome = BattleOutcome.Victory },
      OnFail = new EndBattleDirectiveData { Outcome = BattleOutcome.Defeat },
    });
    return GeoscapeFixture.WithFiredEvent(MakeStart(
      roster:
      [
        MakeEntry("Alpha", MakeCombatantData("Alpha", health: 100)),
        MakeEntry("Beta", MakeCombatantData("Beta", health: 100)),
        MakeEntry("Gamma", MakeCombatantData("Gamma", health: 100)),
        MakeEntry("Delta", MakeCombatantData("Delta", health: 100)),
      ],
      timeline: [MakeScheduled(1, MakeEvent(
        "Ambush", GeoscapeEventKind.TacticalBattle, tacticalMission: mission))]));

  }

  private static BattleUnitState UnitAt(MissionBattle battle, int x) =>
    battle.Runtime.Query(new GetUnitAtTile(
      battle.Runtime.TryGetTile(new Vector3I(x, 0, 0)).RequireSome())).RequireSome();

  // One real action per call through the handle's runtime, minting fresh proofs at use
  // time. Player cells occupy x=0..2; the enemy starts at x=3.
  private static void Strike(MissionBattle battle, BattleUnitState target, int amount,
    DamageKind kind = DamageKind.Health) =>
    battle.Runtime.ExecuteAction(BattleAction.ApplyDamage(
      battle.Runtime.TryGetAlive(target).RequireSome(), amount, kind));

  private static FactionBattleSummary PlayerSummary(MissionBattle battle, CampaignGameState state) =>
    battle.Runtime.Query(new GetFactionEndOfBattleSummary(state.PlayerFaction)).RequireRight();

  private static bool Holds(IReadOnlyList<Combatant> roster, Combatant combatant) =>
    roster.AsValueEnumerable().Any(member => ReferenceEquals(member, combatant));

  [TestCase(TestName = "A rebuilt session commits mixed campaign effects before notifying the return")]
  public static void RebuiltSessionCommitsAllCampaignEffectsBeforeNotifications()
  {
    using var campaign = NewMissionCampaign();
    var mission = campaign.ActiveEvent;
    var alpha = campaign.State.Roster[0];
    var beta = campaign.State.Roster[1];
    var gamma = campaign.State.Roster[2];
    var delta = campaign.State.Roster[3];
    campaign.ReturnFromMission((gamma, 25, 0));
    mission.Definition.AllowUnfitDeployment = true;
    var weapon = MakeWeapon("Service Blade");
    gamma.EquipWeapon(weapon);
    campaign.OpenResolution(mission);
    var battle = campaign.Session.LaunchMission(mission, [alpha, beta, gamma]).RequireRight();
    try
    {
      var grunt = UnitAt(battle, 3).Combatant;
      Strike(battle, UnitAt(battle, 0), 25);
      Strike(battle, UnitAt(battle, 1), 999, DamageKind.Stun);
      Strike(battle, UnitAt(battle, 2), 999);
      Strike(battle, UnitAt(battle, 3), 20, DamageKind.Stun);
      FactionBattleSummary summary = PlayerSummary(battle, campaign.State);
      Assert.Equal(BattleOutcome.Victory, summary.Outcome);

      var rebuilt = new GeoscapeSession(campaign.State);
      Assert.True(ReferenceEquals(battle.Deployment, rebuilt.ActiveMission.RequireSome()));
      Assert.Throws<InvalidOperationException>(() => rebuilt.CompleteResolution(ResolutionOutcome.Engaged));
      bool alphaNotified = false;
      bool betaNotified = false;
      bool closed = false;
      rebuilt.EventCommitted += geoscapeEvent =>
      {
        Assert.Equal(2, campaign.State.Conditions.GetInjury(alpha).RequireSome().Tier);
        Assert.Equal(1, campaign.State.Conditions.GetFatigue(alpha).RequireSome().Tier);
        Assert.True(campaign.State.Conditions.GetInjury(beta).IsNone);
        Assert.Equal(1, campaign.State.Conditions.GetFatigue(beta).RequireSome().Tier);
        Assert.True(Holds(campaign.State.Roster, alpha));
        Assert.True(Holds(campaign.State.Roster, beta));
        Assert.False(Holds(campaign.State.Roster, gamma));
        Assert.True(campaign.State.Conditions.GetInjury(gamma).IsNone);
        Assert.True(campaign.State.Conditions.GetFatigue(gamma).IsNone);
        Assert.True(ReferenceEquals(weapon, gamma.EquippedWeapon.RequireSome()));
        Assert.True(Holds(campaign.State.Roster, delta));
        Assert.True(campaign.State.Conditions.GetInjury(delta).IsNone);
        Assert.True(campaign.State.Conditions.GetFatigue(delta).IsNone);
        Assert.Equal(3, campaign.State.Roster.Count);
        Assert.Equal(1, campaign.State.Captivity.Combatants.Count);
        Assert.True(ReferenceEquals(grunt, campaign.State.Captivity.Combatants[0]));
        Assert.True(ReferenceEquals(grunt.OwningFaction, battle.Setup.Sides[1].Faction));
        Assert.True(campaign.Session.ActiveMission.IsNone);
        Assert.True(campaign.Session.PendingResolution.IsNone);
        Assert.Equal(0, campaign.Session.ActiveEvents.Count);

        Assert.False(closed, "The resolution closes after survivor notifications.");
        if (geoscapeEvent is CombatantConditionsChanged changed)
        {
          if (ReferenceEquals(alpha, changed.Combatant))
          {
            Assert.False(alphaNotified);
            alphaNotified = true;
          }
          else
          {
            Assert.True(ReferenceEquals(beta, changed.Combatant));
            Assert.False(betaNotified);
            betaNotified = true;
          }
        }
        else
        {
          Assert.True(alphaNotified && betaNotified);
          Assert.True(geoscapeEvent is ResolutionEventClosed close
            && ReferenceEquals(close.Resolved.Event, mission)
            && close.SelectedOutcome == ResolutionOutcome.Engaged);
          closed = true;
        }
      };

      rebuilt.CompleteMission(summary);
      Assert.True(closed);
      Assert.True(rebuilt.ActiveMission.IsNone);
      Assert.True(rebuilt.PendingResolution.IsNone);
    }
    finally
    {
      battle.Runtime.Dispose();
    }
  }

  [TestCase(TestName = "Defeat return removes dead participants, forgets their records, and keeps their equipment")]
  public static void DefeatReturnRemovesDeadParticipants()
  {
    using var campaign = NewMissionCampaign();
    var mission = campaign.ActiveEvent;
    var alpha = campaign.State.Roster[0];
    var gamma = campaign.State.Roster[2];
    campaign.ReturnFromMission((alpha, 25, 0));
    Assert.True(campaign.State.Conditions.GetInjury(alpha).IsSome,
      "The seeding return left an active injury behind.");
    mission.Definition.AllowUnfitDeployment = true;
    var weapon = MakeWeapon("Service Blade");
    alpha.EquipWeapon(weapon);
    campaign.OpenResolution(mission);
    var battle = campaign.Session.LaunchMission(mission, [alpha]).RequireRight();
    try
    {
      Strike(battle, UnitAt(battle, 0), 999);
      Assert.Equal(BattleOutcome.Defeat, PlayerSummary(battle, campaign.State).Outcome);

      campaign.ClearEvents();
      campaign.Session.CompleteMission(PlayerSummary(battle, campaign.State));

      Assert.False(Holds(campaign.State.Roster, alpha), "The reported death leaves the roster.");
      Assert.Equal(3, campaign.State.Roster.Count);
      Assert.True(Holds(campaign.State.Roster, gamma), "Nonparticipants are never removed.");
      Assert.True(ReferenceEquals(alpha.EquippedWeapon.RequireSome(), weapon),
        "The dead keep their equipment; nothing returns to the Armory.");
      Assert.True(campaign.State.Conditions.GetFatigue(alpha).IsNone,
        "A removed member's condition records are dropped.");
      Assert.True(campaign.State.Conditions.GetInjury(alpha).IsNone);
      Assert.Equal(0, campaign.State.Captivity.Combatants.Count, "Defeat captures nobody.");

      Assert.True(campaign.Session.ActiveMission.IsNone);
      Assert.True(campaign.Session.PendingResolution.IsNone);
      Assert.Equal(0, campaign.Events.EventsOf<CombatantConditionsChanged>().Length,
        "A dead-only squad notifies no survivor.");
      Assert.Equal(1, campaign.Events.Count);
      Assert.True(campaign.Events[0] is ResolutionEventClosed closed
        && closed.SelectedOutcome == ResolutionOutcome.Engaged);
    }
    finally
    {
      battle.Runtime.Dispose();
    }
  }

  [TestCase(TestName = "A throwing close subscriber leaves the committed return in place")]
  public static void ThrowingSubscriberPropagatesAfterCommit()
  {
    using var campaign = NewMissionCampaign();
    var mission = campaign.ActiveEvent;
    var alpha = campaign.State.Roster[0];
    campaign.OpenResolution(mission);
    var battle = campaign.Session.LaunchMission(mission, [alpha]).RequireRight();
    try
    {
      var grunt = UnitAt(battle, 3).Combatant;
      Strike(battle, UnitAt(battle, 3), 20, DamageKind.Stun);
      Assert.Equal(BattleOutcome.Victory, PlayerSummary(battle, campaign.State).Outcome);

      var retained = new InvalidOperationException("Subscriber exploded.");
      campaign.Session.EventCommitted += geoscapeEvent =>
      {
        if (geoscapeEvent is ResolutionEventClosed)
          throw retained;
      };

      campaign.ClearEvents();
      Exception? caught = null;
      try
      {
        campaign.Session.CompleteMission(PlayerSummary(battle, campaign.State));
      }
      catch (Exception exception)
      {
        caught = exception;
      }

      Assert.True(ReferenceEquals(retained, caught), "The exact subscriber exception propagates.");
      Assert.True(campaign.Session.ActiveMission.IsNone, "Consumption survives the failure.");
      Assert.True(campaign.Session.PendingResolution.IsNone);
      Assert.Equal(1, campaign.State.Captivity.Combatants.Count);
      Assert.True(ReferenceEquals(grunt, campaign.State.Captivity.Combatants[0]));
      Assert.Equal(1, campaign.State.Conditions.GetFatigue(alpha).RequireSome().Tier,
        "Survivor effects stay committed.");
      Assert.Equal(2, campaign.Events.Count, "The close was committed before the throw.");
    }
    finally
    {
      battle.Runtime.Dispose();
    }
  }

}
