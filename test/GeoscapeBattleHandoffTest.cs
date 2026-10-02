#nullable disable warnings
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CampaignGameState = global::FunProject.GameState.GameState;
using FunProject.Battle;
using FunProject.Combatants;
using FunProject.Geoscape;
using FunProject.GameState;
using FunProject.Strategic;
using Godot;
using GdUnit4;
using static FunProject.Tests.GeoscapeTestScenes;

// Real host handoff at the authored scene boundary: mission preparation's Deploy presents the
// battle as a geoscape sibling while the retained stack keeps campaign/session/mission views,
// and the terminal Return applies the campaign return exactly once before restoring the root.
// Battles run on genuinely launched runtimes captured through the production registration
// door; no test-only runtime/state exports exist.

[TestSuite]
[RequireGodotRuntime]
public partial class GeoscapeBattleHandoffTest
{
  // One fired tactical mission with the player-side elimination objective and both terminal
  // directives, so a lethal strike ends the battle. Two player cells fit the squad; one
  // ordinary Grunt spawns at x=2.
  private static TacticalMissionData EliminationMission()
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
    return mission;
  }

  // A mission whose survival objective completes during startup: the launched battle is
  // already terminal when first presented.
  private static TacticalMissionData StartupTerminalMission()
  {
    TacticalMissionData mission = EliminationMission();
    mission.BattleType.Factions[0].Objectives.Clear();
    mission.BattleType.Factions[0].Objectives.Add(new SurviveUntilTurnObjectiveData
    {
      TargetTurn = 1,
      OnComplete = new EndBattleDirectiveData { Outcome = BattleOutcome.Victory },
    });
    return mission;
  }

  private static GeoscapeBattleHandoffScene NewScene(TacticalMissionData mission,
    out RuntimeCaptureSystemData capture, GeoscapeEventDefinition[]? backgroundEvents = null)
  {
    capture = new RuntimeCaptureSystemData();
    mission.BattleType.Systems.Add(capture);
    var scene = ((PackedScene)GD.Load<PackedScene>("res://test/GeoscapeBattleHandoffScene.tscn"))
      .Instantiate<GeoscapeBattleHandoffScene>();
    var timeline = new List<ScheduledEventData>
    {
      MakeScheduled(1, MakeEvent("Ambush", GeoscapeEventKind.TacticalBattle, tacticalMission: mission)),
    };
    foreach (GeoscapeEventDefinition definition in backgroundEvents ?? [])
      timeline.Add(MakeScheduled(1, definition));
    scene.Start = MakeStart(
      roster: [MakeEntry("Alpha", MakeCombatantData("Alpha", health: 100))],
      timeline: [.. timeline]);
    return AddToTree(scene);
  }

  // Fires the mission, captures the scene's live session at the Present boundary, opens
  // its dialog, engages preparation, and selects Alpha. The dialog reference is captured
  // here: after the engage the squad is the stack's current view.
  private static (GeoscapeBattleHandoffScene Scene, GeoscapeViewManager Manager,
    SquadLoadoutView Squad, GeoscapeEventResolution Dialog, GeoscapeSession Session) PreparedScene(
    TacticalMissionData? mission, out RuntimeCaptureSystemData capture,
    GeoscapeEventDefinition[]? backgroundEvents = null)
  {
    var scene = NewScene(mission ?? EliminationMission(), out capture, backgroundEvents);
    var manager = scene.GetNode<GeoscapeViewManager>("%ViewManager");
    SpeedButton(scene).EmitSignal(Button.SignalName.Pressed); // Normal
    scene._PhysicsProcess(0.1); // the mission fires at tick 1
    GeoscapeSession session = CaptureSceneSession(manager); // before the mission flow opens
    OpenResolutionViaMapEvent(scene);
    var dialog = (GeoscapeEventResolution)manager.Current;
    DialogButton(dialog, "Engage").EmitSignal(Button.SignalName.Pressed);
    var squad = (SquadLoadoutView)manager.Current;
    ChooseSquadUnit(squad, 0, "Alpha");
    return (scene, manager, squad, dialog, session);
  }

  // A session-level launch for host-boundary tests that present the runtime themselves.
  private static (GeoscapeFixture Fixture, MissionBattle Battle) LaunchedBattle()
  {
    var capture = new RuntimeCaptureSystemData();
    TacticalMissionData mission = EliminationMission();
    mission.BattleType.Systems.Add(capture);
    var fixture = new GeoscapeFixture(MakeStart(
      roster: [MakeEntry("Alpha", MakeCombatantData("Alpha", health: 100))],
      timeline: [MakeScheduled(1,
        MakeEvent("Ambush", GeoscapeEventKind.TacticalBattle, tacticalMission: mission))]));
    fixture.AdvanceTicks(1);
    fixture.OpenResolution(fixture.ActiveEvent);
    var battle = fixture.Session.LaunchMission(
      fixture.ActiveEvent, [fixture.State.Roster[0]]).RequireRight();
    return (fixture, battle);
  }

  private static BattleUnitState GruntAt(BattleRuntime runtime) =>
    runtime.Query(new GetUnitAtTile(
      runtime.TryGetTile(new Vector3I(2, 0, 0)).RequireSome())).RequireSome();

  [TestCase(TestName = "Deploy presents the battle and Return restores the retained geoscape")]
  public async Task DeployPresentsBattleAndReturnRestoresGeoscape()
  {
    await using var cleanup = new DeferredNodeCleanup();
    var (scene, manager, squad, dialog, _) = PreparedScene(null, out var capture);
    CampaignGameState campaign = scene.Campaign!;
    var session = new GeoscapeSession(campaign); // sessions are rebuildable views over state

    Assert.True(manager.Visible);
    Assert.Equal(1, MapEventMarkers(scene).Length); // the pending mission's marker is live

    SquadDeployButton(squad).EmitSignal(Button.SignalName.Pressed);

    var battle = OnlyChild<BattleScene>(scene); // presented as a sibling of the view manager
    BattleRuntime runtime = capture.Captured!;
    Assert.True(session.ActiveMission.IsSome); // campaign association held while presented
    Assert.True(session.PendingResolution.IsSome);
    Assert.Equal(BattlePhase.InProgress, runtime.Query(new GetBattlePhaseQuery()));
    Assert.False(manager.Visible); // the stack hides; the battle sibling stays live
    Assert.Equal(Node.ProcessModeEnum.Disabled, manager.ProcessMode);
    string clockDuring = Clock(scene).Text;
    scene._PhysicsProcess(1.0); // the pending mission still freezes campaign time
    Assert.Equal(clockDuring, Clock(scene).Text);

    runtime.ExecuteAction(BattleAction.ApplyDamage(
      runtime.TryGetAlive(GruntAt(runtime)).RequireSome(), 999));
    Assert.Equal(BattlePhase.Ended, runtime.Query(new GetBattlePhaseQuery()));

    int intents = 0;
    battle.ReturnRequested += () => intents++; // probe subscribed after the geoscape handler
    var returnButton = ReturnButton(battle);
    Assert.False(returnButton.Visible); // playback still draining
    returnButton.EmitSignal(Button.SignalName.Pressed);
    Assert.Equal(0, intents); // no return intent until the director queue idles

    DrainDirector(battle);
    Assert.True(returnButton.Visible); // terminal and idle at drain

    returnButton.EmitSignal(Button.SignalName.Pressed);
    Assert.Equal(1, intents); // the geoscape handler completed without subscriber failure

    Assert.True(session.ActiveMission.IsNone);
    Assert.True(session.PendingResolution.IsNone);
    Assert.Equal(0, campaign.ActiveEvents.Count); // mission consumed
    Assert.Equal(0, MapEventMarkers(scene).Length); // the consumed mission's marker is gone
    Assert.Equal(1, campaign.Conditions.GetFatigue(campaign.Roster[0]).RequireSome().Tier);
    Assert.True(ReferenceEquals(campaign, scene.Campaign),
      "The scene keeps one campaign instance across presentation and return.");

    Assert.Throws<ObjectDisposedException>(() => runtime.Query(new GetBattlePhaseQuery()));
    Assert.True(manager.Visible);
    Assert.Equal(Node.ProcessModeEnum.Inherit, manager.ProcessMode);
    Assert.True(ReferenceEquals(manager.RootView, manager.Current));

    await WaitForDeferredDeletion((SceneTree)Engine.GetMainLoop());
    Assert.False(GodotObject.IsInstanceValid(squad)); // recorded views removed
    Assert.False(GodotObject.IsInstanceValid(dialog));
    Assert.False(GodotObject.IsInstanceValid(battle)); // host freed after teardown

    // Resumed at the prior speed: the active root ticks the campaign clock again.
    string clockResumed = Clock(scene).Text;
    scene._PhysicsProcess(1.0);
    Assert.That(Clock(scene).Text != clockResumed,
      "The campaign clock must advance again after the return.");
  }

  [TestCase(TestName = "A mission ended at startup offers Return at presentation")]
  public async Task StartupTerminalMissionOffersReturnAtPresentation()
  {

    await using var cleanup = new DeferredNodeCleanup();
    var (scene, manager, squad, _, _) = PreparedScene(StartupTerminalMission(), out var capture);

    SquadDeployButton(squad).EmitSignal(Button.SignalName.Pressed);

    var battle = OnlyChild<BattleScene>(scene);
    BattleRuntime runtime = capture.Captured!;
    Assert.Equal(BattlePhase.Ended, runtime.Query(new GetBattlePhaseQuery()));
    Assert.True(ReturnButton(battle).Visible, // terminal at presentation, director idle
      "Startup-terminal battles must not depend on a later BattleOver transition.");

    int intents = 0;
    battle.ReturnRequested += () => intents++;
    ReturnButton(battle).EmitSignal(Button.SignalName.Pressed);
    Assert.Equal(1, intents);

    var session = new GeoscapeSession(scene.Campaign!);
    Assert.True(session.ActiveMission.IsNone);
    Assert.True(session.PendingResolution.IsNone);
    Assert.Throws<ObjectDisposedException>(() => runtime.Query(new GetBattlePhaseQuery()));
    Assert.True(manager.Visible);
    Assert.True(ReferenceEquals(manager.RootView, manager.Current));
  }

  [TestCase(TestName = "A wrong-root battle export leaves the mission pending with the selection")]
  public async Task WrongRootBattleExportKeepsPreparation()
  {

    await using var cleanup = new DeferredNodeCleanup();
    var (scene, manager, squad, _, _) = PreparedScene(null, out _);

    scene.BattleScene = Pack(new Label { Name = "NotABattleScene" });
    SquadDeployButton(squad).EmitSignal(Button.SignalName.Pressed); // guard throws inside the handler

    var session = new GeoscapeSession(scene.Campaign!);
    Assert.True(session.ActiveMission.IsNone); // no launch association exists
    Assert.True(session.PendingResolution.IsSome);
    Assert.True(ReferenceEquals(squad, manager.Current)); // preparation retained
    Assert.Equal(1, squad.GetSelectedCombatants().Count);
    Assert.Equal(0, scene.GetChildren().AsValueEnumerable().OfType<BattleScene>().Count());
    Assert.True(manager.Visible);
  }

  [TestCase(true, TestName = "A missing %GameCamera in a correctly typed battle export aborts the installation")]
  [TestCase(false, TestName = "A missing %BattleUI in a correctly typed battle export aborts the installation")]
  public async Task CorrectlyTypedInstallFailureAbortsInstallationAndRestoresPreparation(bool dropCamera)
  {
    await using var cleanup = new DeferredNodeCleanup();
    var (scene, manager, squad, _, _) = PreparedScene(null, out var capture);
    CampaignGameState campaign = scene.Campaign!;
    var session = new GeoscapeSession(campaign);
    PendingResolution pending = session.PendingResolution.Match(
      value => value,
      () => throw new InvalidOperationException("The mission must stay pending."));
    Option<int> seedBefore = pending.Event.BattleSeed;

    BattleScene prototype = CreateBattleScene();
    Node authored = prototype.GetNode<Node>(dropCamera ? "GameCamera" : "BattleUI");
    prototype.RemoveChild(authored);
    authored.Free();
    scene.BattleScene = Pack(prototype); // correctly typed root, missing a fallible node

    // Godot's signal dispatch only logs handler exceptions, so the aborted installation is
    // proven by the restored state, not by an escaping exception.
    SquadDeployButton(squad).EmitSignal(Button.SignalName.Pressed);

    BattleRuntime runtime = capture.Captured!;
    Assert.Throws<ObjectDisposedException>(() => runtime.Query(new GetBattlePhaseQuery()));
    Assert.True(session.ActiveMission.IsNone); // association aborted
    Assert.True(session.PendingResolution.IsSome);
    Assert.True(ReferenceEquals(pending,
      session.PendingResolution.Match(value => value, () => null!)));
    Assert.Equal(seedBefore, session.PendingResolution.Match(
      value => value.Event.BattleSeed, () => Option<int>.None)); // seed preserved for a retry
    Assert.Equal(1, squad.GetSelectedCombatants().Count); // selection preserved
    Assert.True(manager.Visible); // the stack is restored
    Assert.Equal(Node.ProcessModeEnum.Inherit, manager.ProcessMode);

    await WaitForDeferredDeletion((SceneTree)Engine.GetMainLoop());
    Assert.Equal(0, scene.GetChildren().AsValueEnumerable().OfType<BattleScene>().Count());
  }

  [TestCase(TestName = "A throwing startup system leaves no detached host and no association")]
  public async Task StartupHookThrowLeavesNoDetachedHost()
  {
    await using var cleanup = new DeferredNodeCleanup();
    TacticalMissionData mission = EliminationMission();
    mission.BattleType.Systems.Add(new ThrowingSystemData());
    var (scene, manager, squad, _, _) = PreparedScene(mission, out _);
    var session = new GeoscapeSession(scene.Campaign!);
    Option<int> seedBefore = session.PendingResolution.Match(
      value => value.Event.BattleSeed,
      () => throw new InvalidOperationException("The mission must stay pending."));

    // The bridge logs the signal-dispatched failure. A probe behind the geoscape handler
    // proves the launch fault aborted the deployment path; the restored state proves the
    // recovery, and the detached host's deferred free keeps the run orphan-free.
    int intents = 0;
    squad.DeployRequested += (_, _) => intents++; // runs only if the geoscape handler completed
    SquadDeployButton(squad).EmitSignal(Button.SignalName.Pressed);

    Assert.Equal(0, intents);
    Assert.True(session.ActiveMission.IsNone); // no launch association was established
    Assert.True(session.PendingResolution.IsSome);
    Assert.Equal(seedBefore, session.PendingResolution.Match(
      value => value.Event.BattleSeed, () => Option<int>.None));
    Assert.Equal(1, squad.GetSelectedCombatants().Count);
    Assert.True(manager.Visible);
    Assert.Equal(0, scene.GetChildren().AsValueEnumerable().OfType<BattleScene>().Count());
  }

  [TestCase(TestName = "Return intent at the host boundary waits for the director queue to idle")]
  public async Task HostBoundaryReturnWaitsForPlaybackIdle()
  {
    await using var cleanup = new DeferredNodeCleanup();
    var (fixture, battle) = LaunchedBattle();
    using var _ = fixture;
    var scene = CreateBattleScene();
    scene.Present(battle.Runtime, battle.Setup, allowReturn: true);
    AddToTree(scene);
    scene.InitializePresentation(); // the installing caller initializes after attachment

    var returnButton = ReturnButton(scene);
    Assert.False(returnButton.Visible); // battle in progress at presentation

    int intents = 0;
    scene.ReturnRequested += () => intents++;
    battle.Runtime.ExecuteAction(BattleAction.ApplyDamage(
      battle.Runtime.TryGetAlive(GruntAt(battle.Runtime)).RequireSome(), 999));
    Assert.Equal(BattlePhase.Ended, battle.Runtime.Query(new GetBattlePhaseQuery()));
    Assert.True(Director(scene).Busy);

    returnButton.EmitSignal(Button.SignalName.Pressed);
    Assert.Equal(0, intents); // the guard holds while playback drains

    DrainDirector(scene);
    Assert.True(returnButton.Visible);

    returnButton.EmitSignal(Button.SignalName.Pressed);
    Assert.Equal(1, intents);

    battle.Runtime.Dispose();
  }

  [TestCase(TestName = "A throwing return subscriber still tears down the presentation once")]
  public async Task ThrowingReturnSubscriberStillTearsDown()
  {

    await using var cleanup = new DeferredNodeCleanup();
    var (scene, manager, squad, dialog, _) = PreparedScene(null, out var capture,
      [MakeEvent("Rumor", GeoscapeEventKind.Plot)]); // stays active so the close refresh renders
    CampaignGameState campaign = scene.Campaign!;
    var session = new GeoscapeSession(campaign);

    SquadDeployButton(squad).EmitSignal(Button.SignalName.Pressed);
    var battle = OnlyChild<BattleScene>(scene);
    BattleRuntime runtime = capture.Captured!;
    runtime.ExecuteAction(BattleAction.ApplyDamage(
      runtime.TryGetAlive(GruntAt(runtime)).RequireSome(), 999));
    DrainDirector(battle);

    // The router's own close-path refresh throws after the return was consumed, so the close
    // event never finishes reaching the scene router; cleanup must still run.
    scene.GetNode<GeoscapeMapControl>("%Map").EventMarkerScene =
      Pack(new Label { Name = "NotAMarker" });

    int probe = 0;
    battle.ReturnRequested += () => probe++; // skipped when the geoscape handler throws

    ReturnButton(battle).EmitSignal(Button.SignalName.Pressed);

    Assert.Equal(0, probe); // the geoscape handler did not complete: the failure propagated
    Assert.True(session.ActiveMission.IsNone);
    Assert.True(session.PendingResolution.IsNone);
    Assert.Equal(1, campaign.ActiveEvents.Count); // the background event stays; the mission is consumed
    Assert.Equal("Rumor", campaign.ActiveEvents.AsValueEnumerable().Single().Definition.Title);
    Assert.Equal(1, campaign.Conditions.GetFatigue(campaign.Roster[0]).RequireSome().Tier,
      "The return must apply exactly once — no replay after the subscriber failure.");
    Assert.Throws<ObjectDisposedException>(() => runtime.Query(new GetBattlePhaseQuery()));
    Assert.True(manager.Visible);
    Assert.Equal(Node.ProcessModeEnum.Inherit, manager.ProcessMode);
    Assert.True(ReferenceEquals(manager.RootView, manager.Current));

    // The throwing close-path refresh cleared every marker before its failed
    // instantiation: the guaranteed cleanup must not resurrect the consumed mission's
    // marker from campaign truth.
    Assert.Equal(0, MapEventMarkers(scene).Length);

    await WaitForDeferredDeletion((SceneTree)Engine.GetMainLoop());
    Assert.False(GodotObject.IsInstanceValid(squad));
    Assert.False(GodotObject.IsInstanceValid(dialog));
    Assert.False(GodotObject.IsInstanceValid(battle));
  }

  [TestCase(TestName = "A conditions subscriber throw before the close still retracts the consumed mission's marker")]
  public async Task ConditionsThrowBeforeCloseStillRetractsConsumedMarker()
  {

    await using var cleanup = new DeferredNodeCleanup();
    var (scene, manager, squad, dialog, liveSession) = PreparedScene(null, out var capture,
      [MakeEvent("Rumor", GeoscapeEventKind.Plot)]); // stays active; its marker must remain
    CampaignGameState campaign = scene.Campaign!;
    var session = new GeoscapeSession(campaign);

    SquadDeployButton(squad).EmitSignal(Button.SignalName.Pressed);
    var battle = OnlyChild<BattleScene>(scene);
    BattleRuntime runtime = capture.Captured!;
    runtime.ExecuteAction(BattleAction.ApplyDamage(
      runtime.TryGetAlive(GruntAt(runtime)).RequireSome(), 999));
    DrainDirector(battle);
    var map = scene.GetNode<GeoscapeMapControl>("%Map");
    int childrenBeforeReturn = map.GetChildCount(); // authored regions plus both markers

    // A commit-stream subscriber ahead of the close explodes on the conditions
    // notification: ResolutionEventClosed never reaches the scene router, so neither the
    // map nor the HUD refreshes after the mission was consumed.
    liveSession.EventCommitted += geoscapeEvent =>
    {
      if (geoscapeEvent is CombatantConditionsChanged)
        throw new InvalidOperationException("Subscriber exploded.");
    };

    int probe = 0;
    battle.ReturnRequested += () => probe++; // skipped when the geoscape handler throws

    ReturnButton(battle).EmitSignal(Button.SignalName.Pressed);

    Assert.Equal(0, probe); // the geoscape handler did not complete: the failure propagated
    Assert.True(session.ActiveMission.IsNone);
    Assert.True(session.PendingResolution.IsNone);
    Assert.Equal(1, campaign.ActiveEvents.Count); // the mission is consumed despite the throw
    Assert.Equal("Rumor", campaign.ActiveEvents.AsValueEnumerable().Single().Definition.Title);
    Assert.Throws<ObjectDisposedException>(() => runtime.Query(new GetBattlePhaseQuery()));
    Assert.True(manager.Visible);
    Assert.Equal(Node.ProcessModeEnum.Inherit, manager.ProcessMode);
    Assert.True(ReferenceEquals(manager.RootView, manager.Current));

    // The guaranteed cleanup retracts the consumed mission's marker without re-stamping
    // (the broken-refresh route must not gain a resurrection path); the background
    // event's marker survives.
    Assert.Equal(childrenBeforeReturn - 1, map.GetChildCount());
    Assert.Equal(1, MapEventMarkers(scene).Length);

    await WaitForDeferredDeletion((SceneTree)Engine.GetMainLoop());
    Assert.False(GodotObject.IsInstanceValid(squad));
    Assert.False(GodotObject.IsInstanceValid(dialog));
    Assert.False(GodotObject.IsInstanceValid(battle));

    // The surviving marker is the background event's and still routes its click.
    GeoscapeEvent? clicked = null;
    map.EventClicked += adapter => clicked = adapter.Event;
    ((RegionButton)map.GetChild(map.GetChildCount() - 1)).EmitSignal(Button.SignalName.Pressed);
    Assert.Equal("Rumor", clicked!.Definition.Title);
    Assert.True(session.PendingResolution.Match(
      value => ReferenceEquals(value.Event, clicked), () => false));
  }
}
