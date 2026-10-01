using System;
using System.Collections.Generic;
using CampaignGameState = global::FunProject.GameState.GameState;
using FunProject.Battle;
using FunProject.Combatants;
using FunProject.Strategic;
using FunProject.Weapons;
using GdUnit4;

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
  // One fired tactical event over a three-soldier campaign. The player side carries the
  // elimination objective with both terminal directives (opposition down = Victory, squad
  // down = Defeat); the enemy side keeps an inert objective. Two player cells and two
  // enemy cells fit a two-soldier squad plus up to one ordinary Grunt and one special Boss.
  private static GeoscapeFixture NewMissionCampaign(bool withSpecialEnemy = false)
  {
    TacticalMissionData mission = MakeTacticalMission(
      MakeTacticalBattleType(playerCells: 2, enemyCells: 2),
      maxPlayerUnits: 2, minEnemyUnits: 1, maxEnemyUnits: 1);
    mission.BattleType.Factions[0].Objectives.Clear();
    mission.BattleType.Factions[0].Objectives.Add(new EliminateAllOpposingForcesObjectiveData
    {
      OnComplete = new EndBattleDirectiveData { Outcome = BattleOutcome.Victory },
      OnFail = new EndBattleDirectiveData { Outcome = BattleOutcome.Defeat },
    });
    if (withSpecialEnemy)
      mission.SpecialEnemies.Add(new UnitLoadoutData
      { Combatant = MakeCombatantData("Boss", health: 40) });
    var campaign = new GeoscapeFixture(MakeStart(
      roster:
      [
        MakeEntry("Alpha", MakeCombatantData("Alpha", health: 100)),
        MakeEntry("Beta", MakeCombatantData("Beta", health: 100)),
        MakeEntry("Gamma", MakeCombatantData("Gamma", health: 100)),
      ],
      timeline: [MakeScheduled(1, MakeEvent(
        "Ambush", GeoscapeEventKind.TacticalBattle, tacticalMission: mission))]));
    try
    {
      campaign.AdvanceTicks(1);
      return campaign;
    }
    catch
    {
      campaign.Dispose();
      throw;
    }
  }

  private static BattleUnitState UnitAt(MissionBattle battle, int x) =>
    battle.Runtime.Query(new GetUnitAtTile(
      battle.Runtime.TryGetTile(new Vector3I(x, 0, 0)).RequireSome())).RequireSome();

  // One real action per call through the handle's runtime, minting fresh proofs at use
  // time. Spawn row order: Alpha (0,0,0), Beta (1,0,0), Grunt (2,0,0), Boss (3,0,0).
  private static void Strike(MissionBattle battle, BattleUnitState target, int amount,
    DamageKind kind = DamageKind.Health) =>
    battle.Runtime.ExecuteAction(BattleAction.ApplyDamage(
      battle.Runtime.TryGetAlive(target).RequireSome(), amount, kind));

  private static FactionBattleSummary PlayerSummary(MissionBattle battle, CampaignGameState state) =>
    battle.Runtime.Query(new GetFactionEndOfBattleSummary(state.PlayerFaction)).RequireRight();

  private static bool Holds(IReadOnlyList<Combatant> roster, Combatant combatant) =>
    roster.AsValueEnumerable().Any(member => ReferenceEquals(member, combatant));

  [TestCase(TestName = "Victory return injures survivors, captures the stunned enemy, and consumes the mission")]
  public void VictoryReturnAppliesAllCampaignEffects()
  {
    using var campaign = NewMissionCampaign();
    var mission = campaign.ActiveEvent;
    var alpha = campaign.State.Roster[0];
    var beta = campaign.State.Roster[1];
    var gamma = campaign.State.Roster[2];
    campaign.OpenResolution(mission);
    var battle = campaign.Session.LaunchMission(mission, [alpha, beta]).RequireRight();
    try
    {
      var grunt = UnitAt(battle, 2).Combatant;
      Strike(battle, UnitAt(battle, 0), 25);
      Strike(battle, UnitAt(battle, 2), 20, DamageKind.Stun);
      Assert.Equal(BattleOutcome.Victory, PlayerSummary(battle, campaign.State).Outcome);

      campaign.ClearEvents();
      campaign.Session.CompleteMission(battle);

      Assert.Equal(2, campaign.State.Conditions.GetInjury(alpha).RequireSome().Tier);
      Assert.Equal(1, campaign.State.Conditions.GetFatigue(alpha).RequireSome().Tier);
      Assert.True(campaign.State.Conditions.GetInjury(beta).IsNone,
        "A stun-only return injures nobody.");
      Assert.Equal(1, campaign.State.Conditions.GetFatigue(beta).RequireSome().Tier,
        "Unconscious survivors earn fatigue like any survivor.");
      Assert.True(campaign.State.Conditions.GetInjury(gamma).IsNone);
      Assert.True(campaign.State.Conditions.GetFatigue(gamma).IsNone);
      Assert.Equal(3, campaign.State.Roster.Count, "Survivors and nonparticipants stay on the roster.");

      Assert.Equal(1, campaign.State.Captivity.Combatants.Count);
      Assert.True(ReferenceEquals(grunt, campaign.State.Captivity.Combatants[0]),
        "The captured enemy keeps its identity.");
      Assert.True(ReferenceEquals(grunt.OwningFaction, battle.Setup.Sides[1].Faction),
        "Capture never changes the enemy's original faction.");

      Assert.True(campaign.Session.ActiveMission.IsNone);
      Assert.True(campaign.Session.PendingResolution.IsNone);
      Assert.Equal(0, campaign.Session.ActiveEvents.Count);

      Assert.Equal(2, campaign.Events.EventsOf<CombatantConditionsChanged>().Length);
      Assert.True(campaign.Events.EventsOf<CombatantConditionsChanged>()
        .AsValueEnumerable().Any(changed => ReferenceEquals(changed.Combatant, alpha)));
      Assert.True(campaign.Events.EventsOf<CombatantConditionsChanged>()
        .AsValueEnumerable().Any(changed => ReferenceEquals(changed.Combatant, beta)));
      Assert.True(campaign.Events[^1] is ResolutionEventClosed closed
        && ReferenceEquals(closed.Resolved.Event, mission)
        && closed.SelectedOutcome == ResolutionOutcome.Engaged,
        "Survivor notices precede the engaged close.");
      Assert.Equal(3, campaign.Events.Count);
    }
    finally
    {
      battle.Runtime.Dispose();
    }
  }

  [TestCase(TestName = "Defeat return removes dead participants, forgets their records, and keeps their equipment")]
  public void DefeatReturnRemovesDeadParticipants()
  {
    using var campaign = NewMissionCampaign();
    var mission = campaign.ActiveEvent;
    var alpha = campaign.State.Roster[0];
    var gamma = campaign.State.Roster[2];
    campaign.ReturnFromMission((alpha, 0, 0));
    var weapon = MakeWeapon("Service Blade");
    alpha.EquipWeapon(weapon);
    campaign.OpenResolution(mission);
    var battle = campaign.Session.LaunchMission(mission, [alpha]).RequireRight();
    try
    {
      Strike(battle, UnitAt(battle, 0), 999);
      Assert.Equal(BattleOutcome.Defeat, PlayerSummary(battle, campaign.State).Outcome);

      campaign.ClearEvents();
      campaign.Session.CompleteMission(battle);

      Assert.False(Holds(campaign.State.Roster, alpha), "The reported death leaves the roster.");
      Assert.Equal(2, campaign.State.Roster.Count);
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

  [TestCase(TestName = "Unconscious survivors stay on the roster and are never captured")]
  public void UnconsciousSurvivorsStayOnRoster()
  {
    using var campaign = NewMissionCampaign();
    var mission = campaign.ActiveEvent;
    var alpha = campaign.State.Roster[0];
    var beta = campaign.State.Roster[1];
    campaign.OpenResolution(mission);
    var battle = campaign.Session.LaunchMission(mission, [alpha, beta]).RequireRight();
    try
    {
      var grunt = UnitAt(battle, 2).Combatant;
      Strike(battle, UnitAt(battle, 1), 999, DamageKind.Stun);
      Strike(battle, UnitAt(battle, 2), 20, DamageKind.Stun);
      Assert.Equal(BattleOutcome.Victory, PlayerSummary(battle, campaign.State).Outcome);

      campaign.ClearEvents();
      campaign.Session.CompleteMission(battle);

      Assert.True(Holds(campaign.State.Roster, beta),
        "Unconscious survivors remain on the roster.");
      Assert.True(campaign.State.Conditions.GetInjury(beta).IsNone);
      Assert.Equal(1, campaign.State.Conditions.GetFatigue(beta).RequireSome().Tier);
      Assert.True(Holds(campaign.State.Roster, alpha));
      Assert.Equal(1, campaign.State.Captivity.Combatants.Count);
      Assert.True(ReferenceEquals(grunt, campaign.State.Captivity.Combatants[0]),
        "Only enemies are captured, never unconscious players.");
      Assert.True(campaign.Session.ActiveMission.IsNone);
      Assert.True(campaign.Session.PendingResolution.IsNone);
    }
    finally
    {
      battle.Runtime.Dispose();
    }
  }

  [TestCase(TestName = "Completing a live battle is denied before any effect")]
  public void EarlyReturnDeniedBeforeEffects()
  {
    using var campaign = NewMissionCampaign();
    var mission = campaign.ActiveEvent;
    var alpha = campaign.State.Roster[0];
    campaign.OpenResolution(mission);
    var battle = campaign.Session.LaunchMission(mission, [alpha]).RequireRight();
    try
    {
      campaign.ClearEvents();
      Assert.Throws<InvalidOperationException>(() => campaign.Session.CompleteMission(battle));

      Assert.True(campaign.Session.ActiveMission.IsSome);
      Assert.True(campaign.Session.PendingResolution.IsSome);
      Assert.Equal(0, campaign.Events.Count, "A denied return commits nothing.");
      Assert.True(campaign.State.Conditions.GetFatigue(alpha).IsNone);
      Assert.Equal(BattlePhase.InProgress, battle.Runtime.Query(new GetBattlePhaseQuery()),
        "The live battle keeps running after the denial.");
    }
    finally
    {
      battle.Runtime.Dispose();
    }
  }

  [TestCase(TestName = "A stale handle after a presentation abort cannot return its battle")]
  public void ForeignHandleDeniedBeforeEffects()
  {
    using var campaign = NewMissionCampaign();
    var mission = campaign.ActiveEvent;
    var alpha = campaign.State.Roster[0];
    campaign.OpenResolution(mission);
    var stale = campaign.Session.LaunchMission(mission, [alpha]).RequireRight();
    Strike(stale, UnitAt(stale, 0), 25);
    Strike(stale, UnitAt(stale, 2), 20, DamageKind.Stun);
    campaign.Session.AbortMissionPresentation(stale);

    var retry = campaign.Session.LaunchMission(mission, [alpha]).RequireRight();
    try
    {
      campaign.ClearEvents();
      Assert.Throws<InvalidOperationException>(() => campaign.Session.CompleteMission(stale));

      Assert.True(ReferenceEquals(retry.Deployment, campaign.Session.ActiveMission.RequireSome()),
        "The retry keeps the association.");
      Assert.True(campaign.Session.PendingResolution.IsSome);
      Assert.True(campaign.State.Conditions.GetInjury(alpha).IsNone,
        "The stale return was never applied.");
      Assert.Equal(0, campaign.State.Captivity.Combatants.Count,
        "The stale return captured nobody.");
      Assert.Equal(0, campaign.Events.Count, "A denied return commits nothing.");
    }
    finally
    {
      retry.Runtime.Dispose();
      stale.Runtime.Dispose();
    }
  }

  [TestCase(TestName = "A consumed mission return cannot apply again")]
  public void ConsumedReturnCannotApplyAgain()
  {
    using var campaign = NewMissionCampaign();
    var mission = campaign.ActiveEvent;
    var alpha = campaign.State.Roster[0];
    var beta = campaign.State.Roster[1];
    campaign.OpenResolution(mission);
    var battle = campaign.Session.LaunchMission(mission, [alpha, beta]).RequireRight();
    try
    {
      Strike(battle, UnitAt(battle, 0), 25);
      Strike(battle, UnitAt(battle, 2), 20, DamageKind.Stun);

      campaign.ClearEvents();
      campaign.Session.CompleteMission(battle);
      Assert.True(campaign.Session.ActiveMission.IsNone);
      Assert.True(campaign.Session.PendingResolution.IsNone);
      FatigueState betaFatigue = campaign.State.Conditions.GetFatigue(beta).RequireSome();

      Assert.Throws<InvalidOperationException>(() => campaign.Session.CompleteMission(battle));

      Assert.Equal(betaFatigue, campaign.State.Conditions.GetFatigue(beta).RequireSome(),
        "The denied second return leaves survivor conditions untouched.");
      Assert.Equal(3, campaign.Events.Count, "The denied second return commits nothing.");
    }
    finally
    {
      battle.Runtime.Dispose();
    }
  }

  [TestCase(TestName = "Return observers see every campaign effect before the first notification")]
  public void ReturnObserversSeeAllCampaignEffects()
  {
    using var campaign = NewMissionCampaign(withSpecialEnemy: true);
    var mission = campaign.ActiveEvent;
    var alpha = campaign.State.Roster[0];
    var beta = campaign.State.Roster[1];
    var gamma = campaign.State.Roster[2];
    campaign.OpenResolution(mission);
    var battle = campaign.Session.LaunchMission(mission, [alpha, beta]).RequireRight();
    try
    {
      var boss = UnitAt(battle, 3).Combatant;
      Strike(battle, UnitAt(battle, 1), 999);
      Strike(battle, UnitAt(battle, 3), 40, DamageKind.Stun);
      Strike(battle, UnitAt(battle, 2), 999);
      Assert.Equal(BattleOutcome.Victory, PlayerSummary(battle, campaign.State).Outcome);

      bool inspectedCondition = false;
      bool inspectedClose = false;
      campaign.Session.EventCommitted += geoscapeEvent =>
      {
        if (geoscapeEvent is CombatantConditionsChanged && !inspectedCondition)
        {
          inspectedCondition = true;
          Assert.False(Holds(campaign.State.Roster, beta), "Deaths are removed before the first notification.");
          Assert.True(Holds(campaign.State.Roster, alpha));
          Assert.True(Holds(campaign.State.Roster, gamma));
          Assert.Equal(1, campaign.State.Conditions.GetFatigue(alpha).RequireSome().Tier,
            "Survivor conditions are applied before the first notification.");
          Assert.Equal(1, campaign.State.Captivity.Combatants.Count);
          Assert.True(ReferenceEquals(boss, campaign.State.Captivity.Combatants[0]),
            "Captures land before the first notification.");
          Assert.True(campaign.Session.ActiveMission.IsNone);
          Assert.True(campaign.Session.PendingResolution.IsNone);
          Assert.Equal(0, campaign.Session.ActiveEvents.Count);
          Assert.Throws<InvalidOperationException>(() => campaign.Session.CompleteMission(battle));
        }
        if (geoscapeEvent is ResolutionEventClosed && !inspectedClose)
        {
          inspectedClose = true;
          Assert.True(campaign.Session.PendingResolution.IsNone);
          Assert.True(campaign.Session.ActiveMission.IsNone);
        }
      };

      campaign.ClearEvents();
      campaign.Session.CompleteMission(battle);

      Assert.True(inspectedCondition);
      Assert.True(inspectedClose);
      Assert.Equal(1, campaign.State.Conditions.GetFatigue(alpha).RequireSome().Tier,
        "The denied reentrant completion accrues no second fatigue tier.");
      Assert.Equal(2, campaign.Events.Count);
    }
    finally
    {
      battle.Runtime.Dispose();
    }
  }

  [TestCase(TestName = "A throwing close subscriber leaves the committed return in place")]
  public void ThrowingSubscriberPropagatesAfterCommit()
  {
    using var campaign = NewMissionCampaign();
    var mission = campaign.ActiveEvent;
    var alpha = campaign.State.Roster[0];
    campaign.OpenResolution(mission);
    var battle = campaign.Session.LaunchMission(mission, [alpha]).RequireRight();
    try
    {
      var grunt = UnitAt(battle, 2).Combatant;
      Strike(battle, UnitAt(battle, 2), 20, DamageKind.Stun);
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
        campaign.Session.CompleteMission(battle);
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

      Assert.Throws<InvalidOperationException>(() => campaign.Session.CompleteMission(battle));
      Assert.Equal(1, campaign.State.Conditions.GetFatigue(alpha).RequireSome().Tier);
      Assert.Equal(1, campaign.State.Captivity.Combatants.Count, "No effect is replayed.");
    }
    finally
    {
      battle.Runtime.Dispose();
    }
  }

  [TestCase(TestName = "A rebuilt session completes the associated mission exactly once")]
  public void RebuiltSessionCompletesAssociatedMission()
  {
    using var campaign = NewMissionCampaign();
    var mission = campaign.ActiveEvent;
    var alpha = campaign.State.Roster[0];
    var beta = campaign.State.Roster[1];
    campaign.OpenResolution(mission);
    var battle = campaign.Session.LaunchMission(mission, [alpha, beta]).RequireRight();
    try
    {
      Strike(battle, UnitAt(battle, 0), 25);
      Strike(battle, UnitAt(battle, 2), 20, DamageKind.Stun);

      var rebuilt = new GeoscapeSession(campaign.State);
      rebuilt.CompleteMission(battle);

      Assert.True(rebuilt.ActiveMission.IsNone);
      Assert.True(rebuilt.PendingResolution.IsNone);
      Assert.True(campaign.Session.ActiveMission.IsNone,
        "The original session observes the shared consumption.");
      Assert.True(campaign.Session.PendingResolution.IsNone);
      Assert.Equal(2, campaign.State.Conditions.GetInjury(alpha).RequireSome().Tier);
      Assert.Equal(3, campaign.State.Roster.Count, "No death means no roster removal.");

      Assert.Throws<InvalidOperationException>(() => campaign.Session.CompleteMission(battle));
    }
    finally
    {
      battle.Runtime.Dispose();
    }
  }
}
