using FunProject.Battle;
using Godot;
using GdUnit4;
using System;
using System.Threading.Tasks;
using static FunProject.Tests.GeoscapeTestScenes;
using static GdUnit4.Assertions;

namespace FunProject.Tests;

// Exercises the BattleLauncher standalone host: an exported BattleTypeData must resolve, start,
// and present a fully wired BattleScene (map, unit meshes, HUD) through the Present door, and
// Present must guard its required inputs.
[TestSuite]
[RequireGodotRuntime]
public partial class BattleLauncherTest
{
  [TestCase(TestName = "Launcher boots the battle scene with map, unit meshes, and populated HUD")]
  public static void LauncherBootsBattleSceneWithMapUnitMeshesAndHud()
  {
    var type = TestData.MakeDuelBattleType();
    var launcher = new BattleLauncher { BattleType = type };
    AddToTree(launcher);

    Assert.Equal(1, launcher.GetChildCount());
    var battle = (BattleScene)launcher.GetChild(0);
    Assert.True(battle.GetNodeOrNull("Map") is not null);
    var unitMeshes = battle.GetNode<Node3D>("UnitMeshes");
    // The duel type deploys exactly one unit per side.
    Assert.Equal(2, unitMeshes.GetChildren().AsValueEnumerable().OfType<MeshInstance3D>().Count());
    var status = battle.GetNode<BattleHud>("BattleUI/BattleHud").GetNode<Label>("%UnitStatus");
    Assert.True(status.Text.Contains("HP", StringComparison.Ordinal));
  }

  [TestCase(TestName = "A failing standalone startup leaves no host and a disposed runtime")]
  public static async Task FailingStartupLeavesNoHostAndDisposedRuntime()
  {
    await using var cleanup = new DeferredNodeCleanup();
    var capture = new HookCaptureSystemData();
    var type = TestData.MakeDuelBattleType();
    type.Systems.Add(capture);
    type.Systems.Add(new ThrowingSystemData());
    var launcher = new BattleLauncher { BattleType = type };
    AddToTree(launcher); // the bridge logs the _Ready failure; nothing may remain half-built

    Assert.Equal(0, launcher.GetChildCount());
    AssertBattleDisposed(capture);
  }

  [TestCase(TestName = "A factory result that already has an outcome shows the battle-over banner at startup")]
  public static void LauncherShowsBannerForStartupTerminalBattle()
  {
    var type = TestData.MakeDuelBattleType();
    type.Factions[0].Objectives.Clear();
    type.Factions[0].Objectives.Add(new SurviveUntilTurnObjectiveData
    {
      TargetTurn = 1,
      OnComplete = new EndBattleDirectiveData { Outcome = BattleOutcome.Victory },
    });
    var launcher = new BattleLauncher { BattleType = type };
    AddToTree(launcher);

    var battle = (BattleScene)launcher.GetChild(0);
    var banner = battle.GetNode<BattleHud>("BattleUI/BattleHud").GetNode<Label>("%BattleOverBanner");
    Assert.True(banner.Visible);
    Assert.Equal("VICTORY", banner.Text);
    Assert.False(ReturnButton(battle).Visible);
    int returns = 0;
    battle.Completed += _ => returns++;
    ReturnButton(battle).EmitSignal(Button.SignalName.Pressed);
    Assert.Equal(0, returns); // terminal standalone battles still cannot return to a campaign
  }

  [TestCase(TestName = "Present guards against nulls")]
  public static void PresentGuardsAgainstNulls()
  {
    var scene = GD.Load<PackedScene>("res://scenes/battle/BattleScene.tscn")
      .Instantiate<BattleScene>();
    AutoFree(scene);

    Assert.Throws<ArgumentNullException>(() => scene.Present(null!, null!));
  }

  [TestCase(TestName = "Disposing an attached host detaches pending playback before runtime teardown")]
  public static async Task DisposeDetachesPendingPlayback()
  {
    var setup = BattleSetupResolver.Resolve(TestData.MakeDuelBattleType()).RequireRight();
    using var runtime = BattleFactory.Start(setup).RequireRight();
    var scene = CreateBattleScene();
    scene.Present(runtime, setup);
    AddToTree(scene);
    scene.InitializePresentation();
    runtime.ExecuteAction(BattleAction.EndFactionTurn(runtime.Query(new GetActiveSideQuery())));
    var director = Director(scene);
    Assert.True(director.Busy);

    scene.Dispose();
    scene.Dispose(); // teardown remains idempotent
    await DrainDirector(scene); // a retained completion must not query the disposed runtime

    Assert.False(director.Busy);
    Assert.Equal(Node.ProcessModeEnum.Disabled, scene.ProcessMode);
    Assert.Throws<ObjectDisposedException>(() => runtime.Query(new GetActiveSideQuery()));
  }

  [TestCase(TestName = "Pointer picking uses the delivered event position even after the cursor moves")]
  public static async Task PointerPickingUsesDeliveredPosition()
  {
    await using var cleanup = new DeferredNodeCleanup();
    var launcher = AddToTree(new BattleLauncher { BattleType = TestData.MakeDuelBattleType() });
    var battle = (BattleScene)launcher.GetChild(0);
    await SelectFirstBattleUnit(battle); // shared collider and physics setup
    battle.GetNode<Node3D>("UnitMeshes").Visible = false; // isolate the terrain-coordinate contract
    await battle.ToSignal(battle.GetTree(), SceneTree.SignalName.PhysicsFrame);
    var camera = battle.GetNode<Camera3D>("GameCamera/CameraPath/PathFollow3D/Camera");
    Vector2 delivered = camera.UnprojectPosition(new Vector3(0.5f, 0, 0.5f));
    battle.GetViewport().WarpMouse(Vector2.Zero);
    GD.Print($"Pointer probe delivered={delivered}, sampled={battle.GetViewport().GetMousePosition()}");
    var input = battle.GetNode<BattleInputController>("BattleInputController");
    Option<Vector3I> clicked = None;
    Option<Vector3I> hovered = None;
    input.TileClicked += tile => clicked = Some(tile);
    input.TileHovered += tile => hovered = Some(tile);

    input._UnhandledInput(new InputEventMouseMotion { Position = delivered });
    input._UnhandledInput(new InputEventMouseButton
    {
      Position = delivered,
      ButtonIndex = MouseButton.Left,
      Pressed = true,
    });

    Assert.Equal(Vector3I.Zero, hovered.RequireSome());
    Assert.Equal(Vector3I.Zero, clicked.RequireSome());
  }

  [TestCase]
  public static async Task PendingPlaybackDefersHudAndDisablesInputUntilDrain()
  {
    await using var cleanup = new DeferredNodeCleanup();
    var setup = BattleSetupResolver.Resolve(TestData.MakeDuelBattleType()).RequireRight();
    using var runtime = BattleFactory.Start(setup).RequireRight();
    var scene = CreateBattleScene();
    scene.Present(runtime, setup);
    AddToTree(scene);
    scene.InitializePresentation();
    Director(scene).SetProcess(false);
    await SelectFirstBattleUnit(scene);
    var status = scene.GetNode<BattleHud>("BattleUI/BattleHud").GetNode<Label>("%UnitStatus");
    string before = status.Text;
    var player = runtime.Query(new GetFactionAliveUnits(setup.PlayerFaction.RequireSome()))
      .AsValueEnumerable().Single();
    runtime.ExecuteAction(BattleAction.ApplyDamage(player, 2));
    Assert.True(Director(scene).Busy);
    Assert.Equal(before, status.Text);
    Assert.True(EndTurnButton(scene).Disabled);
    int submissions = 0;
    runtime.ActionStarted += _ => submissions++;
    EndTurnButton(scene).EmitSignal(Button.SignalName.Pressed);
    Assert.Equal(0, submissions); // programmatic signals must also honor the input gate
    await DrainDirector(scene);
    Assert.False(string.Equals(before, status.Text, StringComparison.Ordinal));
    Assert.True(status.Text.Contains("HP 18/20", StringComparison.Ordinal));
    Assert.False(EndTurnButton(scene).Disabled);
  }

  [TestCase]
  public static void ObserverExitIgnoresAlreadyCapturedResultDeliveryAndLaterInput()
  {
    var setup = BattleSetupResolver.Resolve(TestData.MakeDuelBattleType()).RequireRight();
    using var runtime = BattleFactory.Start(setup).RequireRight();
    var scene = CreateBattleScene();
    scene.Present(runtime, setup);
    AddToTree(scene);
    bool exit = false;
    // Register before presentation delivery: multicast dispatch retains callbacks that
    // this observer unsubscribes while the completed result is already being delivered.
    runtime.ActionCompleted += _ => { if (exit) scene.Dispose(); };
    scene.InitializePresentation();
    var input = scene.GetNode<BattleInputController>("BattleInputController");
    var hud = scene.GetNode<BattleHud>("BattleUI/BattleHud");
    exit = true;
    var action = BattleAction.EndFactionTurn(setup.PlayerFaction.RequireSome());
    var result = runtime.ExecuteAction(action);
    Assert.Equal(action, result.Action);
    Assert.False(Director(scene).Busy);
    hud.GetNode<Button>("%ConfirmButton").EmitSignal(Button.SignalName.Pressed);
    input._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true });
    scene.Dispose();
    Assert.Throws<ObjectDisposedException>(() => runtime.Query(new GetActiveSideQuery()));
  }

  [TestCase]
  public static void InitiallyActiveEnemyPresentationStartsFromSettledFactoryState()
  {
    var type = TestData.MakeDuelBattleType();
    type.Factions[0].Roster[0].Loadout.Combatant = TestData.MakeCombatantData("Player", health: 100);
    type.Factions[1].Roster[0].Loadout.Combatant = TestData.MakeCombatantData("Enemy", aim: 100);
    type.Factions[1].Roster[0].Loadout.Weapon = TestData.MakeWeaponData(damage: 10, critChance: 0, range: 10);
    var setup = BattleSetupResolver.Resolve(type).RequireRight();
    setup = setup with { Sides = [setup.Sides[1], setup.Sides[0]] };
    using var runtime = BattleFactory.Start(setup).RequireRight();
    var scene = CreateBattleScene();
    scene.Present(runtime, setup);
    AddToTree(scene);
    scene.InitializePresentation();
    Director(scene).SetProcess(false);
    var playerFaction = setup.PlayerFaction.RequireSome();
    Assert.Equal(playerFaction, runtime.Query(new GetActiveSideQuery()));
    Assert.False(Director(scene).Busy);
    Assert.Equal(2, scene.GetNode<Node3D>("UnitMeshes").GetChildren()
      .AsValueEnumerable().OfType<MeshInstance3D>().Count());
    var status = scene.GetNode<BattleHud>("BattleUI/BattleHud").GetNode<Label>("%UnitStatus");
    var player = runtime.Query(new GetFactionAliveUnits(playerFaction)).AsValueEnumerable().Single();
    Assert.True(player.State.CurrentHealth < 100);
    Assert.True(status.Text.Contains($"HP {player.State.CurrentHealth}/100", StringComparison.Ordinal));
  }
}
