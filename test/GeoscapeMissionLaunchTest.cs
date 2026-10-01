using System;
using System.Collections.Generic;
using FunProject.Battle;
using FunProject.Combatants;
using FunProject.Stats;
using FunProject.Strategic;
using GdUnit4;

// Campaign mission-launch boundary: typed squad/pending failures against the opened event,
// the captured deployment the battle consumes, and the campaign-held association that guards
// resolution closure and survives session rebuilds. Runtimes returned to tests are
// host-owned and disposed here; the session never registers or disposes one.
[TestSuite]
[RequireGodotRuntime]
public partial class GeoscapeMissionLaunchTest
{
  // One fired tactical event over a three-soldier campaign; the mission's battle type gives
  // each side two spawn cells, with squad capacity and force size set per test.
  private static GeoscapeFixture NewMissionCampaign(
    Action<TacticalMissionData>? customize = null,
    int maxPlayerUnits = 2,
    bool allowUnfitDeployment = false)
  {
    TacticalMissionData mission = MakeTacticalMission(
      MakeTacticalBattleType(playerCells: 2, enemyCells: 2),
      maxPlayerUnits, minEnemyUnits: 1, maxEnemyUnits: 1);
    customize?.Invoke(mission);
    var campaign = new GeoscapeFixture(MakeStart(
      roster: [MakeEntry("Alpha"), MakeEntry("Beta"), MakeEntry("Gamma")],
      timeline: [MakeScheduled(1, MakeEvent(
        "Ambush",
        GeoscapeEventKind.TacticalBattle,
        allowUnfitDeployment: allowUnfitDeployment,
        tacticalMission: mission))]));
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

  // A distinct GeoscapeEvent instance carrying the same definition: the stale request an
  // older dialog or session snapshot would hold.
  private static GeoscapeEvent StaleCopy(GeoscapeEvent mission) =>
    new(mission.Definition, mission.TargetRegionIndex, mission.OccurredTick, mission.ExpiresAtTick);

  [TestCase(MissionLaunchFailureReason.EmptySquad, TestName = "Empty squads do not launch")]
  [TestCase(MissionLaunchFailureReason.DuplicateCombatant, TestName = "Duplicate squad members do not launch")]
  [TestCase(MissionLaunchFailureReason.SquadTooLarge, TestName = "Over-capacity squads do not launch")]
  [TestCase(MissionLaunchFailureReason.NotInRoster, TestName = "Non-roster combatants do not launch")]
  [TestCase(MissionLaunchFailureReason.FactionMismatch, TestName = "Foreign-faction combatants do not launch")]
  [TestCase(MissionLaunchFailureReason.UnitUnavailable, TestName = "Unavailable combatants do not launch")]
  [TestCase(MissionLaunchFailureReason.WrongPendingMission, TestName = "Stale mission requests do not launch")]
  public void InvalidSquadDoesNotLaunch(MissionLaunchFailureReason reason)
  {
    using var campaign = NewMissionCampaign();
    var active = campaign.ActiveEvent;
    if (reason == MissionLaunchFailureReason.UnitUnavailable)
      campaign.ReturnFromMission((campaign.State.Roster[2], 5, 0));
    campaign.OpenResolution(active);
    campaign.ClearEvents();

    var roster = campaign.State.Roster;
    IReadOnlyList<Combatant> squad = reason switch
    {
      MissionLaunchFailureReason.EmptySquad => [],
      MissionLaunchFailureReason.DuplicateCombatant => [roster[0], roster[0]],
      MissionLaunchFailureReason.SquadTooLarge => [roster[0], roster[1], roster[2]],
      MissionLaunchFailureReason.NotInRoster =>
        [roster[0], MakeCombatant("Outsider", campaign.State.PlayerFaction)],
      MissionLaunchFailureReason.FactionMismatch =>
        [roster[0], MakeCombatant("Alien", MakeFaction("Aliens"))],
      MissionLaunchFailureReason.UnitUnavailable => [roster[2]],
      _ => [roster[0], roster[1]],
    };
    GeoscapeEvent requested = reason == MissionLaunchFailureReason.WrongPendingMission
      ? StaleCopy(active)
      : active;

    MissionLaunchFailure failure = campaign.Session.LaunchMission(requested, squad).RequireLeft();

    Assert.Equal(reason, failure.Reason);
    Assert.True(campaign.Session.ActiveMission.IsNone);
    Assert.True(campaign.Session.PendingResolution.IsSome);
    Assert.True(ReferenceEquals(active, campaign.Session.PendingResolution.RequireSome().Event),
      "The pending event must survive a failed launch unchanged.");
    Assert.Equal(0, campaign.Events.Count, "Failed launches commit no geoscape events.");
  }

  [TestCase(TestName = "Launching without an opened resolution fails typed")]
  public void LaunchWithoutOpenedResolutionFailsTyped()
  {
    using var campaign = NewMissionCampaign();
    var active = campaign.ActiveEvent;
    var alpha = campaign.State.Roster[0];

    Assert.Equal(MissionLaunchFailureReason.NoPendingMission,
      campaign.Session.LaunchMission(active, [alpha]).RequireLeft().Reason);

    campaign.OpenResolution(active);
    campaign.CompleteResolution(ResolutionOutcome.Acknowledged);
    Assert.Equal(MissionLaunchFailureReason.NoPendingMission,
      campaign.Session.LaunchMission(active, [alpha]).RequireLeft().Reason);
    Assert.True(campaign.Session.ActiveMission.IsNone);
  }

  [TestCase(TestName = "Launching a pending non-mission event is a caller bug")]
  public void LaunchingNonMissionEventThrows()
  {
    using var campaign = new GeoscapeFixture(MakeStart(
      roster: [MakeEntry("Alpha")],
      timeline: [MakeScheduled(1, MakeEvent("Incident"))]));
    campaign.AdvanceTicks(1);
    var active = campaign.ActiveEvent;
    campaign.OpenResolution(active);

    Assert.Throws<System.InvalidOperationException>(
      () => campaign.Session.LaunchMission(active, [campaign.State.Roster[0]]));
    Assert.True(campaign.Session.ActiveMission.IsNone);
  }

  [TestCase(TestName = "Exceptional deployment captures only condition penalties")]
  public void ExceptionalDeploymentCapturesOnlyConditionPenalties()
  {
    using var campaign = NewMissionCampaign(allowUnfitDeployment: true);
    var active = campaign.ActiveEvent;
    var alpha = campaign.State.Roster[0];
    campaign.ReturnFromMission((alpha, 5, 0));
    var weapon = MakeWeapon("Service Blade");
    alpha.EquipWeapon(weapon);
    var armor = MakeArmor("Vest");
    alpha.EquipArmor(armor);
    campaign.OpenResolution(active);
    Assert.True(campaign.State.Conditions.GetInjury(alpha).IsSome,
      "The scenario must seed an injury for the override to waive.");
    Assert.False(campaign.State.Conditions.CanDeploy(alpha, allowUnfitDeployment: false));
    Assert.True(campaign.Session.CanDeploy(alpha, active.Definition));

    var battle = campaign.Session.LaunchMission(active, [alpha]).RequireRight();
    try
    {
      Assert.True(ReferenceEquals(campaign.State.PlayerFaction, battle.Setup.Sides[0].Faction));
      UnitLoadout loadout = battle.Setup.Sides[0].Units[0].Loadout;
      Assert.True(ReferenceEquals(alpha, loadout.Combatant));
      Assert.True(ReferenceEquals(weapon, loadout.Weapon.RequireSome()),
        "The loadout carries the combatant's live weapon reference.");
      Assert.True(ReferenceEquals(armor.Item, loadout.Armor.RequireSome().Item),
        "The loadout carries the combatant's live armor reference.");

      IReadOnlyList<StatMod> expected = campaign.State.Conditions.StatContributions(alpha);
      Assert.Equal(expected.Count, loadout.StatMods.Count);
      for (int index = 0; index < expected.Count; index++)
        Assert.True(ReferenceEquals(expected[index], loadout.StatMods[index]),
          $"Deployment stat mod {index} must be the condition contribution itself; faction/weapon/progression stats stay out of the loadout.");
    }
    finally
    {
      battle.Runtime.Dispose();
    }
  }

  [TestCase(TestName = "Successful launch associates the deployment and retains pending truth")]
  public void SuccessfulLaunchRetainsAssociationAndPendingTruth()
  {
    using var campaign = NewMissionCampaign(customize: mission => mission.SpecialEnemies.Add(
      new UnitLoadoutData { Combatant = MakeCombatantData("Boss", health: 40) }));
    var active = campaign.ActiveEvent;
    var alpha = campaign.State.Roster[0];
    var beta = campaign.State.Roster[1];
    campaign.OpenResolution(active);

    var selected = new List<Combatant> { alpha, beta };
    campaign.ClearEvents();
    var battle = campaign.Session.LaunchMission(active, selected).RequireRight();
    try
    {
      selected.Clear();

      var deployment = campaign.Session.ActiveMission.RequireSome();
      Assert.True(ReferenceEquals(battle.Deployment, deployment));
      Assert.True(ReferenceEquals(active, deployment.Event),
        "The association keeps the exact pending event.");
      Assert.Equal(2, deployment.Participants.Count);
      Assert.True(ReferenceEquals(alpha, deployment.Participants[0]));
      Assert.True(ReferenceEquals(beta, deployment.Participants[1]),
        "Captured participants keep selection order and ignore later caller mutations.");

      BattleSideSetup player = battle.Setup.Sides[0];
      Assert.True(ReferenceEquals(campaign.State.PlayerFaction, player.Faction));
      Assert.Equal(2, player.Units.Count);
      Assert.True(ReferenceEquals(alpha, player.Units[0].Loadout.Combatant));
      Assert.True(ReferenceEquals(beta, player.Units[1].Loadout.Combatant));

      BattleSideSetup enemies = battle.Setup.Sides[1];
      Assert.Equal(2, enemies.Units.Count);
      Assert.Equal("Grunt", enemies.Units[0].Loadout.Combatant.Name);
      Assert.Equal("Boss", enemies.Units[1].Loadout.Combatant.Name);
      foreach (UnitPlacement placement in enemies.Units)
        Assert.True(ReferenceEquals(enemies.Faction, placement.Loadout.Combatant.OwningFaction));

      Assert.True(ReferenceEquals(active, campaign.Session.PendingResolution.RequireSome().Event));
      Assert.Equal(1, campaign.Session.Tick, "Launch must not advance campaign time.");
      Assert.True(campaign.Session.ActiveEvents.AsValueEnumerable()
        .Any(member => ReferenceEquals(member, active)),
        "The pending event stays active until the mission returns.");
      Assert.Equal(active.BattleSeed.RequireSome(), battle.Setup.Seed,
        "The battle resolves with the event's retained generation seed.");
      Assert.Equal(0, campaign.Events.Count, "Launch commits no geoscape events.");
    }
    finally
    {
      battle.Runtime.Dispose();
    }
  }

  [TestCase(TestName = "Setup shortfall retains the typed failure and the pending event")]
  public void SetupShortfallRetainsTypedFailureAndPending()
  {
    using var campaign = NewMissionCampaign(maxPlayerUnits: 3);
    var active = campaign.ActiveEvent;
    campaign.OpenResolution(active);

    MissionLaunchFailure failure = campaign.Session.LaunchMission(active,
      [campaign.State.Roster[0], campaign.State.Roster[1], campaign.State.Roster[2]]).RequireLeft();

    Assert.Equal(MissionLaunchFailureReason.SetupFailed, failure.Reason);
    Assert.Equal(BattleSetupFailureReason.SpawnSlotShortfall, failure.SetupFailure.RequireSome().Reason);
    Assert.True(campaign.Session.ActiveMission.IsNone);
    Assert.True(ReferenceEquals(active, campaign.Session.PendingResolution.RequireSome().Event));
  }

  [TestCase(TestName = "A throwing initialization propagates without registering a launch")]
  public void ThrowingInitializationPropagatesWithoutAssociation()
  {
    var boom = new InvalidOperationException("Initialization exploded.");
    using var campaign = NewMissionCampaign(customize: mission =>
      mission.BattleType.Systems.Add(new ThrowingSystemData { Failure = boom }));
    var active = campaign.ActiveEvent;
    campaign.OpenResolution(active);

    Exception? caught = null;
    try
    {
      campaign.Session.LaunchMission(active, [campaign.State.Roster[0]]);
    }
    catch (Exception exception)
    {
      caught = exception;
    }

    Assert.True(ReferenceEquals(boom, caught),
      "The original initialization exception must propagate unchanged.");
    Assert.True(campaign.Session.ActiveMission.IsNone);
    Assert.True(campaign.Session.PendingResolution.IsSome);
    Assert.True(ReferenceEquals(active, campaign.Session.PendingResolution.RequireSome().Event));
  }

  [TestCase(TestName = "Launch and resolution guards hold across a session rebuild")]
  public void GuardsHoldAcrossSessionRebuild()
  {
    using var campaign = NewMissionCampaign();
    var active = campaign.ActiveEvent;
    var squad = new[] { campaign.State.Roster[0], campaign.State.Roster[1] };
    campaign.OpenResolution(active);
    var battle = campaign.Session.LaunchMission(active, squad).RequireRight();
    try
    {
      Assert.Equal(MissionLaunchFailureReason.AlreadyLaunched,
        campaign.Session.LaunchMission(active, squad).RequireLeft().Reason);
      Assert.Throws<System.InvalidOperationException>(
        () => campaign.Session.CompleteResolution(ResolutionOutcome.Engaged));
      Assert.Throws<System.InvalidOperationException>(
        () => campaign.Session.CompleteResolution(ResolutionOutcome.Declined));

      var rebuilt = new GeoscapeSession(campaign.State);
      Assert.True(ReferenceEquals(battle.Deployment, rebuilt.ActiveMission.RequireSome()));
      Assert.Equal(MissionLaunchFailureReason.AlreadyLaunched,
        rebuilt.LaunchMission(active, squad).RequireLeft().Reason);
      Assert.Throws<System.InvalidOperationException>(
        () => rebuilt.CompleteResolution(ResolutionOutcome.Acknowledged));
    }
    finally
    {
      battle.Runtime.Dispose();
    }
  }

  [TestCase(TestName = "Presentation abort clears only the association and retains the pending mission")]
  public void AbortMissionPresentationRetainsPendingMission()
  {
    using var campaign = NewMissionCampaign();
    var active = campaign.ActiveEvent;
    int seed = active.BattleSeed.RequireSome();
    var squad = new[] { campaign.State.Roster[0], campaign.State.Roster[1] };
    campaign.OpenResolution(active);
    var battle = campaign.Session.LaunchMission(active, squad).RequireRight();

    Assert.Throws<ArgumentNullException>(
      () => campaign.Session.AbortMissionPresentation(null!));

    new GeoscapeSession(campaign.State).AbortMissionPresentation(battle);

    Assert.True(campaign.Session.ActiveMission.IsNone);
    Assert.True(campaign.Session.PendingResolution.IsSome);
    Assert.True(ReferenceEquals(active, campaign.Session.PendingResolution.RequireSome().Event));
    Assert.Equal(seed, active.BattleSeed.RequireSome(), "The retained event keeps its generation seed.");
    Assert.True(campaign.Session.ActiveEvents.AsValueEnumerable()
      .Any(member => ReferenceEquals(member, active)));
    Assert.True(battle.Runtime.TryGetTile(Vector3I.Zero).IsSome,
      "Abort must not dispose the host-owned runtime.");

    Assert.Throws<System.InvalidOperationException>(
      () => campaign.Session.AbortMissionPresentation(battle));

    try
    {
      var retry = campaign.Session.LaunchMission(active, squad).RequireRight();
      retry.Runtime.Dispose();
      Assert.True(campaign.Session.ActiveMission.IsSome,
        "A presentation abort allows a later retry of the retained mission.");
    }
    finally
    {
      battle.Runtime.Dispose();
    }
  }

  // Test-local authored system whose registration faults, driving BattleFactory's startup
  // failure path through LaunchMission.
  private sealed partial class ThrowingSystemData : BattleTypeSystemData
  {
    public required Exception Failure { get; set; }

    public override void Register(BattleRuntime runtime) => throw Failure;
  }
}
