using System;
using System.Collections.Generic;
using FunProject.Battle;
using FunProject.Combatants;
using FunProject.Stats;
using FunProject.Strategic;
using GdUnit4;

// Mission selection normalization, startup snapshots, setup failures, and campaign associations.
// Tests dispose their host-owned runtimes.
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

  [TestCase(0, TestName = "Duplicate selections retain the first occurrence in order")]
  [TestCase(1, TestName = "Over-capacity selections retain the first capacity-sized squad")]
  [TestCase(2, TestName = "Deduplication precedes capacity truncation")]
  [TestCase(3, TestName = "Unsigned maximum capacity preserves the selected squad")]
  public void SelectionNormalizesInOrder(int selectionKind)
  {
    using var campaign = NewMissionCampaign(customize: mission =>
    {
      if (selectionKind == 3)
        mission.Size.MaxPlayerUnits = uint.MaxValue;
    });
    var roster = campaign.State.Roster;
    Combatant first = roster[2];
    Combatant second = roster[1];
    IReadOnlyList<Combatant> selected = selectionKind switch
    {
      0 => [first, first, second],
      1 => [first, second, roster[0]],
      3 => [first, second],
      _ => [first, first, second, roster[0]],
    };
    campaign.OpenResolution(campaign.ActiveEvent);

    var battle = campaign.Session.LaunchMission(campaign.ActiveEvent, selected).RequireRight();
    try
    {
      Assert.Equal(2, battle.Deployment.Participants.Count);
      Assert.True(ReferenceEquals(first, battle.Deployment.Participants[0]));
      Assert.True(ReferenceEquals(second, battle.Deployment.Participants[1]));
      Assert.Equal(2, battle.Setup.Sides[0].Units.Count);
      Assert.True(ReferenceEquals(first, battle.Setup.Sides[0].Units[0].Loadout.Combatant));
      Assert.True(ReferenceEquals(second, battle.Setup.Sides[0].Units[1].Loadout.Combatant));
    }
    finally
    {
      battle.Runtime.Dispose();
    }
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

  [TestCase(TestName = "Startup selection mutation cannot alter the validated deployment")]
  public void StartupSelectionMutationKeepsValidatedDeployment()
  {
    var mutation = new ScriptedSystemData();
    using var campaign = NewMissionCampaign(customize: mission =>
      mission.BattleType.Systems.Add(mutation));
    var active = campaign.ActiveEvent;
    var alpha = campaign.State.Roster[0];
    var beta = campaign.State.Roster[1];
    campaign.OpenResolution(active);

    var selected = new List<Combatant> { alpha, alpha, beta, campaign.State.Roster[2] };
    mutation.OnRegister = () =>
    {
      selected.Clear();
      selected.Add(campaign.State.Roster[2]);
    };

    var battle = campaign.Session.LaunchMission(active, selected).RequireRight();
    try
    {
      var deployment = campaign.Session.ActiveMission.RequireSome();
      Assert.True(ReferenceEquals(battle.Deployment, deployment),
        "The association is stored only after startup succeeded.");
      Assert.Equal(2, deployment.Participants.Count);
      Assert.True(ReferenceEquals(alpha, deployment.Participants[0]));
      Assert.True(ReferenceEquals(beta, deployment.Participants[1]),
        "Captured membership is the validated selection, not the callback-mutated caller list.");
      BattleSideSetup player = battle.Setup.Sides[0];
      Assert.Equal(2, player.Units.Count);
      Assert.True(ReferenceEquals(alpha, player.Units[0].Loadout.Combatant));
      Assert.True(ReferenceEquals(beta, player.Units[1].Loadout.Combatant),
        "The battle setup still deploys the original validated selection.");
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

    BattleSetupFailure failure = campaign.Session.LaunchMission(active,
      [campaign.State.Roster[0], campaign.State.Roster[1], campaign.State.Roster[2]]).RequireLeft();

    Assert.Equal(BattleSetupFailureReason.SpawnSlotShortfall, failure.Reason);
    Assert.True(campaign.Session.ActiveMission.IsNone);
    Assert.True(ReferenceEquals(active, campaign.Session.PendingResolution.RequireSome().Event));
  }

  [TestCase(TestName = "A throwing initialization propagates without registering a launch")]
  public void ThrowingInitializationPropagatesWithoutAssociation()
  {
    var boom = new InvalidOperationException("Initialization exploded.");
    using var campaign = NewMissionCampaign(customize: mission =>
      mission.BattleType.Systems.Add(new ScriptedSystemData { OnRegister = () => throw boom }));
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

  [TestCase(TestName = "Mission association and resolution ownership survive a session rebuild")]
  public void AssociationSurvivesSessionRebuild()
  {
    using var campaign = NewMissionCampaign();
    var active = campaign.ActiveEvent;
    var squad = new[] { campaign.State.Roster[0], campaign.State.Roster[1] };
    campaign.OpenResolution(active);
    var battle = campaign.Session.LaunchMission(active, squad).RequireRight();
    try
    {
      Assert.Throws<System.InvalidOperationException>(
        () => campaign.Session.CompleteResolution(ResolutionOutcome.Engaged));
      Assert.Throws<System.InvalidOperationException>(
        () => campaign.Session.CompleteResolution(ResolutionOutcome.Declined));

      var rebuilt = new GeoscapeSession(campaign.State);
      Assert.True(ReferenceEquals(battle.Deployment, rebuilt.ActiveMission.RequireSome()));
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
    try
    {
      new GeoscapeSession(campaign.State).AbortMissionPresentation();

      Assert.True(campaign.Session.ActiveMission.IsNone);
      Assert.True(campaign.Session.PendingResolution.IsSome);
      Assert.True(ReferenceEquals(active, campaign.Session.PendingResolution.RequireSome().Event));
      Assert.Equal(seed, active.BattleSeed.RequireSome(), "The retained event keeps its generation seed.");
      Assert.True(campaign.Session.ActiveEvents.AsValueEnumerable()
        .Any(member => ReferenceEquals(member, active)));
      Assert.True(battle.Runtime.TryGetTile(Vector3I.Zero).IsSome,
        "Abort must not dispose the host-owned runtime.");

      var retry = campaign.Session.LaunchMission(active, squad).RequireRight();
      try
      {
        Assert.True(campaign.Session.ActiveMission.IsSome,
          "A presentation abort allows a later retry of the retained mission.");
      }
      finally
      {
        retry.Runtime.Dispose();
      }
    }
    finally
    {
      battle.Runtime.Dispose();
    }
  }

  // Test-local authored system scripting registration behavior, driving BattleFactory's
  // startup callbacks through LaunchMission.
  private sealed partial class ScriptedSystemData : BattleTypeSystemData
  {
    public Action? OnRegister { get; set; }

    public override void Register(BattleHookRegistration hooks) => OnRegister?.Invoke();
  }
}
