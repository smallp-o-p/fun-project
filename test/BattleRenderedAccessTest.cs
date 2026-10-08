using System.Threading.Tasks;
using FunProject.Battle;
using GdUnit4;
using Godot;
using static FunProject.Tests.GeoscapeTestScenes;

[TestSuite]
[RequireGodotRuntime]
public sealed partial class BattleRenderedAccessTest
{
  private static BattleSetup Setup()
  {
    var player = TestData.MakeFaction("Player");
    var enemy = TestData.MakeFaction("Enemy");
    var map = TestData.MakeOpenBattleMap(new Vector3I(6, 1, 6));
    return TestData.MakeBattleSetup(player, enemy,
      new UnitLoadout(TestData.MakeCombatant("Alpha", player, actionPoints: 5)),
      new UnitLoadout(TestData.MakeCombatant("Guard", enemy)),
      [new FakeObjectiveData()], [new FakeObjectiveData()], Some(player)) with
    { Map = map, MapScene = Some(TestData.MakeMapScene(map)) };
  }

  private static BattleScene Present(BattleRuntime runtime, BattleSetup setup)
  {
    var scene = CreateBattleScene();
    scene.Present(runtime, setup, allowReturn: true);
    AddToTree(scene);
    scene.InitializePresentation();
    return scene;
  }

  private static async Task Frames(int count)
  {
    var tree = (SceneTree)Engine.GetMainLoop();
    for (int i = 0; i < count; i++) await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
  }

  private static void Pointer(BattleScene scene, Vector3I tile, bool click = false)
  {
    var camera = scene.GetNode<Camera3D>("GameCamera/CameraPath/PathFollow3D/Camera");
    var point = camera.UnprojectPosition(BoardCoordinates.TileToWorldCenter(tile));
    scene.GetViewport().WarpMouse(point);
    var input = scene.GetNode<BattleInputController>("BattleInputController");
    input._UnhandledInput(click
      ? new InputEventMouseButton { Position = point, ButtonIndex = MouseButton.Left, Pressed = true }
      : new InputEventMouseMotion { Position = point });
  }

  [TestCase]
  public async Task RealTweenAndLatestHoverSurviveWorkerReadAndHostRemoval()
  {
    await using var cleanup = new DeferredNodeCleanup();
    var setup = Setup();
    using var runtime = BattleFactory.Start(setup).RequireRight();
    var scene = Present(runtime, setup);
    var unit = runtime.Query(new GetUnitAtTile(runtime.TryGetTile(Vector3I.Zero).RequireSome())).RequireSome();
    var ground = new StaticBody3D { Position = new Vector3(3, -0.05f, 3) };
    ground.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(6, 0.1f, 6) } });
    scene.AddChild(ground);
    await Frames(3);
    Pointer(scene, Vector3I.Zero, click: true);
    var hud = scene.GetNode<BattleHud>("BattleUI/BattleHud");
    var move = hud.GetNode<Container>("%VerbButtons").GetChildren().AsValueEnumerable()
      .OfType<Button>().Single(button => button.Text == "Move");
    move.EmitSignal(Button.SignalName.Pressed);
    var line = scene.GetNode<MovementLine>("BattleUI/MovementLine");
    Pointer(scene, new Vector3I(1, 0, 0));
    Assert.True(line.Visible);
    var previous = line.Points[^1];
    using (BattleConcurrencyProbe.HoldQuery(runtime))
    {
      Pointer(scene, new Vector3I(2, 0, 0));
      Pointer(scene, new Vector3I(2, 0, 1));
      await Frames(3);
      Assert.Equal(previous, line.Points[^1]);
    }
    await Frames(3);
    Assert.Equal(BoardCoordinates.TileToWorldCenter(new Vector3I(2, 0, 1)) + new Vector3(0, 0.05f, 0), line.Points[^1]);

    // Submit through the authored HUD/input boundary, then hold an admitted worker while
    // the production playback director moves the real mesh through a native Tween.
    Pointer(scene, new Vector3I(2, 0, 1), click: true);
    hud.GetNode<Button>("%ConfirmButton").EmitSignal(Button.SignalName.Pressed);
    var mesh = scene.GetNode<BattleUnitMeshes>("UnitMeshes").GetChild<Node3D>(0);
    var beforePosition = mesh.Position;
    var status = hud.GetNode<Label>("%UnitStatus").Text;
    var tweenStartTicks = System.Diagnostics.Stopwatch.GetTimestamp();
    using (BattleConcurrencyProbe.HoldQuery(runtime))
    {
      // Uncapped rendering can advance 45 frames before the time-based tween travels half a tile.
      while (mesh.Position.DistanceTo(beforePosition) <= 0.5f
        && System.Diagnostics.Stopwatch.GetElapsedTime(tweenStartTicks) < System.TimeSpan.FromSeconds(2))
        await Frames(1);
      Assert.True(mesh.Position.DistanceTo(beforePosition) > 0.5f,
        $"After {System.Diagnostics.Stopwatch.GetElapsedTime(tweenStartTicks).TotalMilliseconds:F1} ms, mesh moved {mesh.Position.DistanceTo(beforePosition):F3} from {beforePosition} to {mesh.Position}.");
      Assert.Equal(status, hud.GetNode<Label>("%UnitStatus").Text);
      GD.Print($"Guarded rendered probe: native mesh moved {mesh.Position.DistanceTo(beforePosition):F3}; HUD retained; latest hover restored.");
      if (DisplayServer.GetName() != "headless")
      {
        await scene.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using var pixels = scene.GetViewport().GetTexture().GetImage();
        Assert.Equal(Error.Ok, pixels.SavePng("user://guarded-battle-probe.png"));
        GD.Print($"Guarded rendered probe image: {OS.GetUserDataDir()}/guarded-battle-probe.png");
      }
      scene.QueueFree();
      await Frames(3);
      Assert.True(runtime.IsClosing);
      Assert.False(runtime.TryFinishDisposal());
      Assert.True(unit.Combatant.IsDeployedInBattle);
    }
    await Frames(5);
    Assert.False(unit.Combatant.IsDeployedInBattle);
    Assert.True(runtime.TryFinishDisposal());
  }

  [TestCase]
  public async Task BusyPlaybackIdleRetriesEnemyTurnOnceAndReturnWaitsForDrain()
  {
    await using var cleanup = new DeferredNodeCleanup();
    var setup = Setup();
    using var battle = new BattleFixture(setup);
    var runtime = battle.Runtime;
    var scene = Present(runtime, setup);
    var player = setup.PlayerFaction.RequireSome();
    runtime.ExecuteAction(BattleAction.EndFactionTurn(player));
    int submitted = 0;
    runtime.ActionStarted += _ => submitted++;
    using (BattleConcurrencyProbe.HoldQuery(runtime))
    {
      DrainDirector(scene);
      await Frames(3);
      Assert.Equal(0, submitted);
    }
    await Frames(5);
    DrainDirector(scene);
    Assert.Equal(1, submitted);
    Assert.True(ReferenceEquals(player, runtime.Query(new GetActiveSideQuery())));

    battle.Mutate(() => battle.Session.EndBattle(BattleOutcome.Victory));
    DrainDirector(scene);
    var unit = runtime.Query(new GetFactionAliveUnits(player)).AsValueEnumerable().Single().State;
    int completed = 0;
    scene.Completed += _ =>
    {
      Assert.False(unit.Combatant.IsDeployedInBattle);
      completed++;
    };
    using (BattleConcurrencyProbe.HoldQuery(runtime))
    {
      ReturnButton(scene).EmitSignal(Button.SignalName.Pressed);
      await Frames(3);
      Assert.Equal(0, completed);
      Assert.True(unit.Combatant.IsDeployedInBattle);
    }
    await Frames(5);
    ReturnButton(scene).EmitSignal(Button.SignalName.Pressed);
    Assert.Equal(1, completed);
  }
}
