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
  // Captures the launched runtime via BattleFactory's system registration callback, so
  // scene-boundary tests can prove ownership transfer and disposal without new seams.
  private sealed partial class RuntimeCaptureSystemData : BattleTypeSystemData
  {
    public BattleRuntime? Captured { get; private set; }

    public override void Register(BattleRuntime runtime) => Captured = runtime;
  }

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

  // Fires the mission, opens its dialog, engages preparation, and selects Alpha. The dialog
  // reference is captured here: after the engage the squad is the stack's current view.
  private static (GeoscapeBattleHandoffScene Scene, GeoscapeViewManager Manager,
    SquadLoadoutView Squad, GeoscapeEventResolution Dialog) PreparedScene(
    TacticalMissionData? mission, out RuntimeCaptureSystemData capture,
    GeoscapeEventDefinition[]? backgroundEvents = null)
  {
    var scene = NewScene(mission ?? EliminationMission(), out capture, backgroundEvents);
    var manager = scene.GetNode<GeoscapeViewManager>("%ViewManager");
    SpeedButton(scene).EmitSignal(Button.SignalName.Pressed); // Normal
    scene._PhysicsProcess(0.1); // the mission fires at tick 1
    OpenResolutionViaAlert(scene);
    var dialog = (GeoscapeEventResolution)manager.Current;
    DialogButton(dialog, "Engage").EmitSignal(Button.SignalName.Pressed);
    var squad = (SquadLoadoutView)manager.Current;
    ChooseSquadUnit(squad, 0, "Alpha");
    return (scene, manager, squad, dialog);
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
    var (scene, manager, squad, dialog) = PreparedScene(null, out var capture);
    CampaignGameState campaign = scene.Campaign!;
    var session = new GeoscapeSession(campaign); // sessions are rebuildable views over state

    Assert.True(manager.Visible);
    Assert.Equal(1, AlertCount(scene));

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
    Assert.Equal(0, AlertCount(scene));
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

    // BISECT: clock-resume step removed
  }

  [TestCase(TestName = "A mission ended at startup offers Return at presentation")]
  public async Task StartupTerminalMissionOffersReturnAtPresentation()
  {

    await using var cleanup = new DeferredNodeCleanup();
    var (scene, manager, squad, _) = PreparedScene(StartupTerminalMission(), out var capture);

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
    var (scene, manager, squad, _) = PreparedScene(null, out _);

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

  [TestCase(TestName = "Return intent at the host boundary waits for the director queue to idle")]
  public async Task HostBoundaryReturnWaitsForPlaybackIdle()
  {
    await using var cleanup = new DeferredNodeCleanup();
    var (fixture, battle) = LaunchedBattle();
    using var _ = fixture;
    var scene = CreateBattleScene();
    scene.Present(battle.Runtime, battle.Setup, allowReturn: true);
    AddToTree(scene);

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
    var (scene, manager, squad, dialog) = PreparedScene(null, out var capture,
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

    await WaitForDeferredDeletion((SceneTree)Engine.GetMainLoop());
    Assert.False(GodotObject.IsInstanceValid(squad));
    Assert.False(GodotObject.IsInstanceValid(dialog));
    Assert.False(GodotObject.IsInstanceValid(battle));
  }
}
