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

namespace FunProject.Tests;

// Real host handoff at the authored scene boundary: mission preparation's Deploy presents the
// battle inside the pushed mission view while the stack keeps the campaign and session,
// and terminal Return applies the campaign return once, shows results, then Continue restores the root.
// Battles advance through real HUD actions; startup registration exposes only the hook
// capability used to verify owner disposal. Campaign effect details live in return tests.

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

  // A real turn transition completes the authored objective; turn 1 covers battles
  // already terminal at startup and turn 2 covers playback and normal Return.
  private static TacticalMissionData SurvivalMission(int targetTurn = 2)
  {
    TacticalMissionData mission = EliminationMission();
    mission.BattleType.Factions[0].Objectives.Clear();
    mission.BattleType.Factions[0].Objectives.Add(new SurviveUntilTurnObjectiveData
    {
      TargetTurn = targetTurn,
      OnComplete = new EndBattleDirectiveData { Outcome = BattleOutcome.Victory },
    });
    return mission;
  }

  private static GeoscapeBattleHandoffScene NewScene(TacticalMissionData mission,
    out HookCaptureSystemData capture, GeoscapeEventDefinition[]? backgroundEvents = null, Vector2I? windowSize = null)
  {
    capture = new HookCaptureSystemData();
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
    if (windowSize is { } size)
    {
      CreateScaledWindow(scene, size);
      return scene;
    }
    return AddToTree(scene);
  }

  // Fires the mission, captures the scene's live session at the Present boundary, opens
  // its dialog, engages preparation, and selects Alpha. The dialog reference is captured
  // here: after the engage the squad is the stack's current view.
  private static (GeoscapeBattleHandoffScene Scene, GeoscapeViewManager Manager,
    SquadLoadoutView Squad, GeoscapeEventResolution Dialog, GeoscapeSession Session) PreparedScene(
    TacticalMissionData? mission, out HookCaptureSystemData capture,
    GeoscapeEventDefinition[]? backgroundEvents = null, Vector2I? windowSize = null)
  {
    var scene = NewScene(mission ?? SurvivalMission(), out capture, backgroundEvents, windowSize);
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

  private static void ContinueResults(GeoscapeView mission)
    => mission.GetNode<Control>("MissionResultsView").GetNode<Button>("%ContinueButton")
      .EmitSignal(Button.SignalName.Pressed);

  [TestCase(TestName = "Terminal return retains results and frozen time until Continue")]
  public static async Task TerminalReturnRetainsResults()
  {
    await using var cleanup = new DeferredNodeCleanup();
    var (scene, manager, squad, _, session) = PreparedScene(SurvivalMission(1), out var capture);
    SquadDeployButton(squad).EmitSignal(Button.SignalName.Pressed);
    var battle = MissionBattleHost(squad);
    ReturnButton(battle).EmitSignal(Button.SignalName.Pressed);

    var results = squad.GetNode<Control>("MissionResultsView");
    Assert.True(results.IsVisibleInTree());
    Assert.True(ReferenceEquals(squad, manager.Current));
    Assert.Equal("1", results.GetNode<Label>("%TurnsTaken").Text);
    Assert.True(session.ActiveMission.IsNone);
    Assert.True(session.PendingResolution.IsNone);
    AssertBattleDisposed(capture);
    long tick = session.Tick;
    scene._PhysicsProcess(1.0);
    Assert.Equal(tick, session.Tick);
    ContinueResults(squad);
    Assert.True(ReferenceEquals(manager.RootView, manager.Current));
    scene._PhysicsProcess(0.1);
    Assert.True(session.Tick > tick);
  }

  [TestCase(800, 600, TestName = "Opaque battle viewport covers retained geoscape at minimum size")]
  [TestCase(1600, 900, TestName = "Opaque battle viewport covers retained geoscape in a scaled window")]
  public static async Task BattleOwnsOpaqueViewport(int width, int height)
  {
    await using var cleanup = new DeferredNodeCleanup();
    var (scene, manager, squad, _, session) = PreparedScene(SurvivalMission(1), out var capture,
      windowSize: new Vector2I(width, height));
    var sentinel = new ColorRect { Color = Colors.Magenta, MouseFilter = Control.MouseFilterEnum.Ignore };
    manager.RootView.AddChild(sentinel);
    sentinel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
    SquadDeployButton(squad).EmitSignal(Button.SignalName.Pressed);
    var battle = MissionBattleHost(manager.Current);
    Assert.True(ReturnButton(battle).Visible);
    Assert.True(manager.RootView.IsVisibleInTree());

    Assert.True(battle.GetViewport() is SubViewport);
    var viewport = (SubViewport)battle.GetViewport();
    var container = (SubViewportContainer)viewport.GetParent();
    Assert.False(viewport.TransparentBg);
    Assert.True(viewport.OwnWorld3D);
    Assert.True(container.Stretch);
    Assert.True(container.IsVisibleInTree());
    Assert.Equal(Node.ProcessModeEnum.Disabled, manager.RootView.ProcessMode);
    await WaitForLayout(squad);
    Assert.Equal(ScreenRect(squad), ScreenRect(container));
    Assert.Equal(new Vector2I((int)container.Size.X, (int)container.Size.Y), viewport.Size);

    await RenderingServer.Singleton.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
    using var pixels = viewport.GetTexture().GetImage();
    Assert.Equal(1f, pixels.GetPixel(0, 0).A);
    Assert.Equal(1f, pixels.GetPixel(pixels.GetWidth() - 1, pixels.GetHeight() - 1).A);
    var window = squad.GetWindow();
    using var rendered = window.GetTexture().GetImage();
    Assert.False(rendered.GetPixel(1, 1).IsEqualApprox(Colors.Magenta));
    Assert.False(rendered.GetPixel(rendered.GetWidth() - 2, rendered.GetHeight() - 2).IsEqualApprox(Colors.Magenta));
    Option<BattleUnitState> clicked = None;
    battle.GetNode<BattleInputController>("BattleInputController").UnitClicked += unit => clicked = Some(unit);
    await SelectFirstBattleUnit(battle, throughContainer: true);
    Assert.True(ReferenceEquals(scene.Campaign!.Roster[0], clicked.RequireSome().Combatant));
    int completions = 0;
    battle.Completed += _ => completions++;
    ReturnButton(battle).EmitSignal(Button.SignalName.Pressed);
    Assert.Equal(1, completions);
    Assert.True(session.ActiveMission.IsNone);
    Assert.True(session.PendingResolution.IsNone);
    AssertBattleDisposed(capture);
    ContinueResults(squad);
    Assert.True(ReferenceEquals(manager.RootView, manager.Current));
    await WaitForLayout(window);
    using var restored = window.GetTexture().GetImage();
    Assert.True(restored.GetPixel(1, 1).IsEqualApprox(Colors.Magenta));
  }

  [TestCase(TestName = "Two deployments restore the same geoscape and route fresh input")]
  public static async Task DeployPresentsBattleAndReturnRestoresGeoscape()
  {
    await using var cleanup = new DeferredNodeCleanup();
    var secondMission = MakeEvent("Second mission", GeoscapeEventKind.TacticalBattle,
      tacticalMission: SurvivalMission());
    var (scene, manager, squad, dialog, liveSession) = PreparedScene(null, out var capture,
      backgroundEvents: [secondMission]);
    CampaignGameState campaign = scene.Campaign!;
    var session = new GeoscapeSession(campaign); // sessions are rebuildable views over state

    Assert.True(manager.Visible);
    Assert.Equal(2, MapEventMarkers(scene).Length);

    SquadDeployButton(squad).EmitSignal(Button.SignalName.Pressed);

    var battle = MissionBattleHost(manager.Current); // owned by the pushed mission view
    Assert.True(session.ActiveMission.IsSome); // campaign association held while presented
    Assert.True(session.PendingResolution.IsSome);
    Assert.False(ReturnButton(battle).Visible);
    Assert.True(manager.Visible);
    Assert.Equal(Node.ProcessModeEnum.Inherit, manager.ProcessMode);
    Assert.False(squad.GetNode<Control>("Content").Visible);
    int cancelInputs = 0;
    battle.GetNode<BattleInputController>("BattleInputController").CancelPressed += () => cancelInputs++;
    scene.GetViewport().PushInput(new InputEventMouseButton
    {
      Position = new Vector2(400, 200),
      ButtonIndex = MouseButton.Right,
      Pressed = true,
    });
    Assert.Equal(1, cancelInputs); // retained geoscape must not consume battle pointer input
    scene.GetViewport().PushInput(new InputEventMouseButton
    {
      Position = new Vector2(400, 200),
      ButtonIndex = MouseButton.Right,
      Pressed = false,
    });
    string clockDuring = Clock(scene).Text;
    scene._PhysicsProcess(1.0); // the pending mission still freezes campaign time
    Assert.Equal(clockDuring, Clock(scene).Text);

    await SelectFirstBattleUnit(battle);
    EndTurnButton(battle).EmitSignal(Button.SignalName.Pressed);

    int intents = 0;
    battle.Completed += _ => intents++; // probe subscribed after the mission handler
    var returnButton = ReturnButton(battle);
    Assert.False(returnButton.Visible); // terminal playback has not completed
    returnButton.EmitSignal(Button.SignalName.Pressed);
    Assert.Equal(0, intents); // an ongoing battle cannot return

    await DrainDirector(battle);
    Assert.True(returnButton.Visible); // terminal and idle at drain

    returnButton.EmitSignal(Button.SignalName.Pressed);
    Assert.Equal(1, intents); // the mission handler completed without subscriber failure

    var results = squad.GetNode<Control>("MissionResultsView");
    Assert.True(results.IsVisibleInTree());
    Assert.Equal("2", results.GetNode<Label>("%TurnsTaken").Text);
    Assert.True(ReferenceEquals(squad, manager.Current));
    AssertBattleDisposed(capture);
    scene._PhysicsProcess(1.0);
    Assert.Equal(clockDuring, Clock(scene).Text, "Results must freeze time after mission consumption.");
    ContinueResults(squad);

    Assert.True(session.ActiveMission.IsNone);
    Assert.True(session.PendingResolution.IsNone);
    Assert.Equal(1, campaign.ActiveEvents.Count); // only the deployed mission was consumed
    Assert.Equal(1, MapEventMarkers(scene).Length);
    Assert.Equal(1, campaign.Conditions.GetFatigue(campaign.Roster[0]).RequireSome().Tier);
    Assert.True(ReferenceEquals(campaign, scene.Campaign),
      "The scene keeps one campaign instance across presentation and return.");

    Assert.True(manager.Visible);
    Assert.Equal(Node.ProcessModeEnum.Inherit, manager.ProcessMode);
    Assert.True(ReferenceEquals(manager.RootView, manager.Current));

    await WaitForDeferredDeletion((SceneTree)Engine.GetMainLoop());
    Assert.False(GodotObject.IsInstanceValid(squad)); // mission view popped
    Assert.False(GodotObject.IsInstanceValid(dialog));
    Assert.False(GodotObject.IsInstanceValid(battle)); // host freed after teardown

    // Resumed at the prior speed: the active root ticks the campaign clock again.
    string clockResumed = Clock(scene).Text;
    scene._PhysicsProcess(1.0);
    Assert.That(!string.Equals(Clock(scene).Text, clockResumed, StringComparison.Ordinal),
      "The campaign clock must advance again after the return.");

    OpenResolutionViaMapEvent(scene);
    var secondDialog = (GeoscapeEventResolution)manager.Current;
    DialogButton(secondDialog, "Engage").EmitSignal(Button.SignalName.Pressed);
    var secondSquad = (SquadLoadoutView)manager.Current;
    ChooseSquadUnit(secondSquad, 0, "Alpha");
    Assert.True(ReferenceEquals(liveSession, CaptureSceneSession(manager)));
    SquadDeployButton(secondSquad).EmitSignal(Button.SignalName.Pressed);
    var secondBattle = MissionBattleHost(manager.Current);
    await SelectFirstBattleUnit(secondBattle);
    EndTurnButton(secondBattle).EmitSignal(Button.SignalName.Pressed);
    await DrainDirector(secondBattle);
    ReturnButton(secondBattle).EmitSignal(Button.SignalName.Pressed);
    ContinueResults(secondSquad);
    Assert.Equal(0, campaign.ActiveEvents.Count);
    Assert.Equal(0, MapEventMarkers(scene).Length);
    Assert.True(session.ActiveMission.IsNone);
    Assert.True(ReferenceEquals(manager.RootView, manager.Current));
    await WaitForDeferredDeletion((SceneTree)Engine.GetMainLoop());
    Assert.False(GodotObject.IsInstanceValid(secondBattle));
    Assert.False(GodotObject.IsInstanceValid(secondSquad));
  }

  [TestCase(TestName = "A wrong-root battle export leaves the mission pending with the selection")]
  public static async Task WrongRootBattleExportKeepsPreparation()
  {

    await using var cleanup = new DeferredNodeCleanup();
    var (scene, manager, squad, _, _) = PreparedScene(null, out _);

    ((MissionView)squad).BattleScene = Pack(new Label { Name = "NotABattleScene" });
    SquadDeployButton(squad).EmitSignal(Button.SignalName.Pressed); // guard throws inside the handler

    var session = new GeoscapeSession(scene.Campaign!);
    Assert.True(session.ActiveMission.IsNone); // no launch association exists
    Assert.True(session.PendingResolution.IsSome);
    Assert.True(ReferenceEquals(squad, manager.Current)); // preparation retained
    Assert.Equal(1, squad.GetSelectedCombatants().Count);
    Assert.Equal(0, squad.GetNode<SubViewport>("Background/Viewport").GetChildCount());
    Assert.True(manager.Visible);
  }

  [TestCase("GameCamera", TestName = "A missing %GameCamera in a correctly typed battle export aborts the installation")]
  [TestCase("BattleUI", TestName = "A missing %BattleUI in a correctly typed battle export aborts the installation")]
  [TestCase("BattleUI/BattleHud", TestName = "A partially ready battle UI without its HUD still disposes the runtime")]
  public static async Task CorrectlyTypedInstallFailureAbortsInstallationAndRestoresPreparation(string missingNode)
  {
    await using var cleanup = new DeferredNodeCleanup();
    var (scene, manager, squad, _, _) = PreparedScene(SurvivalMission(1), out var capture);
    CampaignGameState campaign = scene.Campaign!;
    var session = new GeoscapeSession(campaign);
    PendingResolution pending = session.PendingResolution.Match(
      value => value,
      () => throw new InvalidOperationException("The mission must stay pending."));
    Option<int> seedBefore = pending.Event.BattleSeed;

    BattleScene prototype = CreateBattleScene();
    Node authored = prototype.GetNode<Node>(missingNode);
    authored.GetParent().RemoveChild(authored);
    authored.Free();
    if (string.Equals(missingNode, "BattleUI/BattleHud", StringComparison.Ordinal))
    {
      // Inline this nested instance: packing an instance otherwise restores deleted
      // children from its original scene, so the requested missing-HUD case would be lost.
      var partialUi = prototype.GetNode<BattleUI>("BattleUI");
      partialUi.SceneFilePath = "";
      foreach (Node child in partialUi.GetChildren()) child.Owner = prototype;
    }
    ((MissionView)squad).BattleScene = Pack(prototype); // correctly typed root, missing a fallible node

    // Godot's signal dispatch only logs handler exceptions, so the aborted installation is
    // proven by the restored state, not by an escaping exception.
    SquadDeployButton(squad).EmitSignal(Button.SignalName.Pressed);

    AssertBattleDisposed(capture);
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
    Assert.Equal(0, squad.GetNode<SubViewport>("Background/Viewport").GetChildCount());

    // Repairing the authored host retries the same pending event with the retained squad.
    ((MissionView)squad).BattleScene = GD.Load<PackedScene>("res://scenes/battle/BattleScene.tscn");
    SquadDeployButton(squad).EmitSignal(Button.SignalName.Pressed);
    var retry = MissionBattleHost(manager.Current);
    Assert.True(session.ActiveMission.IsSome);
    ReturnButton(retry).EmitSignal(Button.SignalName.Pressed);
    ContinueResults(squad);
    Assert.True(session.ActiveMission.IsNone);
    Assert.True(session.PendingResolution.IsNone);
    Assert.True(ReferenceEquals(manager.RootView, manager.Current));
    AssertBattleDisposed(capture);
  }

  [TestCase(TestName = "A throwing startup system leaves no detached host and no association")]
  public static async Task StartupHookThrowLeavesNoDetachedHost()
  {
    await using var cleanup = new DeferredNodeCleanup();
    TacticalMissionData mission = EliminationMission();
    mission.BattleType.Systems.Add(new ThrowingSystemData());
    var (scene, manager, squad, _, _) = PreparedScene(mission, out _);
    var session = new GeoscapeSession(scene.Campaign!);
    Option<int> seedBefore = session.PendingResolution.Match(
      value => value.Event.BattleSeed,
      () => throw new InvalidOperationException("The mission must stay pending."));

    // The bridge logs signal-dispatched failures; inspect restored campaign and host ownership.
    SquadDeployButton(squad).EmitSignal(Button.SignalName.Pressed);

    Assert.True(session.ActiveMission.IsNone); // no launch association was established
    Assert.True(session.PendingResolution.IsSome);
    Assert.Equal(seedBefore, session.PendingResolution.Match(
      value => value.Event.BattleSeed, () => Option<int>.None));
    Assert.Equal(1, squad.GetSelectedCombatants().Count);
    Assert.True(manager.Visible);
    Assert.Equal(0, squad.GetNode<SubViewport>("Background/Viewport").GetChildCount());
  }

  [TestCase(TestName = "Return intent at the host boundary waits for the director queue to idle")]
  public static async Task HostBoundaryReturnWaitsForPlaybackIdle()
  {
    await using var cleanup = new DeferredNodeCleanup();
    using var fixture = new GeoscapeFixture(MakeStart(
      roster: [MakeEntry("Alpha", MakeCombatantData("Alpha", health: 100))],
      timeline: [MakeScheduled(1, MakeEvent("Ambush", GeoscapeEventKind.TacticalBattle,
        tacticalMission: EliminationMission()))]));
    fixture.AdvanceTicks(1);
    fixture.OpenResolution(fixture.ActiveEvent);
    var battle = fixture.Session.LaunchMission(fixture.ActiveEvent, [fixture.State.Roster[0]]).RequireRight();
    using var runtime = battle.Runtime;
    var scene = CreateBattleScene();
    scene.Present(battle.Runtime, battle.Setup, allowReturn: true);
    AddToTree(scene);
    scene.InitializePresentation(); // the installing caller initializes after attachment

    var returnButton = ReturnButton(scene);
    Assert.False(returnButton.Visible); // battle in progress at presentation

    int intents = 0;
    scene.Completed += _ => intents++;
    var grunt = runtime.Query(new GetUnitAtTile(runtime.TryGetTile(new Vector3I(2, 0, 0)).RequireSome())).RequireSome();
    runtime.ExecuteAction(BattleAction.ApplyDamage(runtime.TryGetAlive(grunt).RequireSome(), 999));
    Assert.True(battle.Runtime.Query(new GetBattleOutcomeQuery()).IsSome);
    Assert.True(Director(scene).Busy);
    var banner = scene.GetNode<BattleHud>("BattleUI/BattleHud").GetNode<Label>("%BattleOverBanner");
    Assert.False(banner.Visible);

    returnButton.EmitSignal(Button.SignalName.Pressed);
    Assert.Equal(0, intents); // the guard holds while playback drains

    await DrainDirector(scene);
    Assert.True(returnButton.Visible);
    Assert.True(banner.Visible);
    Assert.Equal("VICTORY", banner.Text);

    returnButton.EmitSignal(Button.SignalName.Pressed);
    Assert.Equal(1, intents);

  }

  [TestCase(TestName = "A throwing return subscriber still tears down the presentation once")]
  public static async Task ThrowingReturnSubscriberStillTearsDown()
  {

    await using var cleanup = new DeferredNodeCleanup();
    var (scene, manager, squad, dialog, _) = PreparedScene(null, out var capture,
      [MakeEvent("Rumor", GeoscapeEventKind.Plot)]); // stays active so the close refresh renders
    CampaignGameState campaign = scene.Campaign!;
    var session = new GeoscapeSession(campaign);

    SquadDeployButton(squad).EmitSignal(Button.SignalName.Pressed);
    var battle = MissionBattleHost(manager.Current);
    await SelectFirstBattleUnit(battle);
    EndTurnButton(battle).EmitSignal(Button.SignalName.Pressed);
    await DrainDirector(battle);

    // Captured before the swap: MapEventMarkers filters by the export's resource path, so
    // after substituting the wrong-root scene its count could not see the original markers.
    var markersBeforeReturn = MapEventMarkers(scene);

    // The router's own close-path refresh throws after the return was consumed, so the close
    // event never finishes reaching the scene router; cleanup must still run.
    scene.GetNode<GeoscapeMapControl>("%Map").EventMarkerScene =
      Pack(new Label { Name = "NotAMarker" });

    int probe = 0;
    battle.Completed += _ => probe++; // skipped when the mission handler throws

    ReturnButton(battle).EmitSignal(Button.SignalName.Pressed);

    Assert.Equal(0, probe); // the mission handler did not complete: the failure propagated
    Assert.True(session.ActiveMission.IsNone);
    Assert.True(session.PendingResolution.IsNone);
    Assert.Equal(1, campaign.ActiveEvents.Count); // the background event stays; the mission is consumed
    Assert.Equal("Rumor", campaign.ActiveEvents.AsValueEnumerable().Single().Definition.Title);
    Assert.Equal(1, campaign.Conditions.GetFatigue(campaign.Roster[0]).RequireSome().Tier,
      "The return must apply exactly once — no replay after the subscriber failure.");
    AssertBattleDisposed(capture);
    Assert.True(manager.Visible);
    Assert.Equal(Node.ProcessModeEnum.Inherit, manager.ProcessMode);
    Assert.True(squad.GetNode<Control>("MissionResultsView").IsVisibleInTree());
    ContinueResults(squad);
    Assert.True(ReferenceEquals(manager.RootView, manager.Current));

    // The throwing close-path refresh detached every original marker before its failed
    // instantiation: the guaranteed cleanup must not resurrect the consumed mission's
    // marker from campaign truth. Asserted by node identity — the path-filtered count is
    // vacuous once the export was swapped.
    foreach (var marker in markersBeforeReturn)
      Assert.True(marker.GetParent() is null);

    await WaitForDeferredDeletion((SceneTree)Engine.GetMainLoop());
    Assert.False(GodotObject.IsInstanceValid(squad));
    Assert.False(GodotObject.IsInstanceValid(dialog));
    Assert.False(GodotObject.IsInstanceValid(battle));
  }

  [TestCase(TestName = "A conditions subscriber throw before the close still retracts the consumed mission's marker")]
  public static async Task ConditionsThrowBeforeCloseStillRetractsConsumedMarker()
  {

    await using var cleanup = new DeferredNodeCleanup();
    var (scene, manager, squad, dialog, liveSession) = PreparedScene(null, out var capture,
      [MakeEvent("Rumor", GeoscapeEventKind.Plot)]); // stays active; its marker must remain
    CampaignGameState campaign = scene.Campaign!;
    var session = new GeoscapeSession(campaign);

    SquadDeployButton(squad).EmitSignal(Button.SignalName.Pressed);
    var battle = MissionBattleHost(manager.Current);
    await SelectFirstBattleUnit(battle);
    EndTurnButton(battle).EmitSignal(Button.SignalName.Pressed);
    await DrainDirector(battle);
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
    battle.Completed += _ => probe++; // skipped when the mission handler throws

    ReturnButton(battle).EmitSignal(Button.SignalName.Pressed);

    Assert.Equal(0, probe); // the mission handler did not complete: the failure propagated
    Assert.True(session.ActiveMission.IsNone);
    Assert.True(session.PendingResolution.IsNone);
    Assert.Equal(1, campaign.ActiveEvents.Count); // the mission is consumed despite the throw
    Assert.Equal("Rumor", campaign.ActiveEvents.AsValueEnumerable().Single().Definition.Title);
    AssertBattleDisposed(capture);
    Assert.True(manager.Visible);
    Assert.Equal(Node.ProcessModeEnum.Inherit, manager.ProcessMode);
    Assert.True(squad.GetNode<Control>("MissionResultsView").IsVisibleInTree());
    ContinueResults(squad);
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
