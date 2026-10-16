using FunProject.Battle;
using Godot;
using GdUnit4;
using System;
using System.Threading.Tasks;
using static FunProject.Tests.GeoscapeTestScenes;

namespace FunProject.Tests;

// Owns the visible-body input contract at the real viewport / BattleScene boundary.
// The synthetic map has no collider: a ray aimed at the upper body must hit the unit itself.
[TestSuite]
[RequireGodotRuntime]
public partial class BattlePickingTest
{
  [TestCase]
  public static async Task BodyClicksSelectFriendAndAttackEnemyExactlyOnce()
  {
    await using var cleanup = new DeferredNodeCleanup();
    var (battle, runtime, _) = CreateBattle();
    await WaitForPhysics(battle);
    var input = battle.GetNode<BattleInputController>("BattleInputController");
    int tileClicks = 0;
    int attacks = 0;
    input.TileClicked += _ => tileClicks++;
    runtime.ActionStarted += action => { if (action is AttackEntity) attacks++; };

    ClickBody(battle, new Vector3(1.5f, 0.85f, 0.5f));
    Assert.Equal(0, Verbs(battle).GetChildCount(), "Enemies cannot be selected as actors.");
    ClickBody(battle, new Vector3(0.5f, 0.85f, 0.5f));
    Assert.True(Verbs(battle).GetChildCount() > 0, "Clicking the visible friendly body must select it.");
    await WaitForLayout(battle);
    AttackButton(battle).EmitSignal(Button.SignalName.Pressed);
    ClickBody(battle, new Vector3(0.5f, 0.85f, 0.5f));
    Assert.Equal(0, attacks, "Attack targeting retains its existing hostile-only eligibility.");
    ClickBody(battle, new Vector3(1.5f, 0.85f, 0.5f), click: false);
    Assert.True(battle.GetNode<BattleHud>("BattleUI/BattleHud").GetNode<Label>("%HitChance").Visible);
    ClickBody(battle, new Vector3(1.5f, 0.85f, 0.5f));
    Assert.Equal(1, attacks, "A body click must submit just one attack.");
    Assert.Equal(0, tileClicks, "A unit hit must not also dispatch a tile click.");
    await DrainDirector(battle);
  }

  [TestCase]
  public static async Task GuiAndTerrainOccludeBodiesAndHiddenBodiesLeaveTilePickingAvailable()
  {
    await using var cleanup = new DeferredNodeCleanup();
    var (battle, _, _) = CreateBattle();
    var camera = Camera(battle);
    Vector3 body = new(0.5f, 0.85f, 0.5f);
    Vector2 pointer = camera.UnprojectPosition(body);
    var overlay = new Panel { Position = pointer - new Vector2(25, 25), Size = new Vector2(50, 50) };
    battle.GetNode<BattleHud>("BattleUI/BattleHud").AddChild(overlay);
    await WaitForLayout(battle);
    ClickBody(battle, body);
    Assert.Equal(0, Verbs(battle).GetChildCount(), "GUI controls consume clicks before world picking.");
    overlay.Free();

    var wall = new StaticBody3D { Position = camera.GlobalPosition.Lerp(body, 0.25f) };
    wall.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = Vector3.One * 0.6f } });
    battle.AddChild(wall);
    await WaitForPhysics(battle);
    ClickBody(battle, body);
    Assert.Equal(0, Verbs(battle).GetChildCount(), "Nearer terrain must block a unit hit.");
    wall.Free();
    await WaitForPhysics(battle);
    ClickBody(battle, body);
    Assert.True(Verbs(battle).GetChildCount() > 0);

    var mesh = battle.GetNode<Node3D>("UnitMeshes").GetChild<MeshInstance3D>(0);
    mesh.Visible = false;
    var ground = new StaticBody3D { Position = new Vector3(1, -0.05f, 0) };
    ground.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(20, 0.1f, 20) } });
    battle.AddChild(ground);
    int tileClicks = 0;
    battle.GetNode<BattleInputController>("BattleInputController").TileClicked += _ => tileClicks++;
    await WaitForPhysics(battle);
    ClickBody(battle, body);
    Assert.Equal(1, tileClicks, "An invisible body's collider must not swallow the ground click.");

  }

  [TestCase]
  public static async Task UnitPickingSupportsOrthographicCameraTransform()
  {
    await using var cleanup = new DeferredNodeCleanup();
    var (battle, _, _) = CreateBattle();
    var camera = Camera(battle);
    camera.GlobalPosition = new Vector3(-3, 3, 2);
    camera.LookAt(new Vector3(1, 0.5f, 0.5f));
    camera.Projection = Camera3D.ProjectionType.Orthogonal;
    camera.Size = 5;
    await WaitForPhysics(battle);
    ClickBody(battle, new Vector3(0.5f, 0.85f, 0.5f));
    Assert.True(Verbs(battle).GetChildCount() > 0);
  }

  [TestCase]
  public static async Task PickingFollowsMovementAndRetiresDeadBodiesAfterPlayback()
  {
    await using var cleanup = new DeferredNodeCleanup();
    var (battle, runtime, _) = CreateBattle(TestData.MakeTacticalBattleType(playerCells: 2));
    var playerFaction = runtime.Query(new GetPlayerFactionQuery()).RequireSome();
    var player = runtime.Query(new GetFactionAliveUnits(playerFaction)).AsValueEnumerable().Single();
    var input = battle.GetNode<BattleInputController>("BattleInputController");
    Option<BattleUnitState> clicked = None;
    input.UnitClicked += unit => clicked = Some(unit);
    await WaitForPhysics(battle);
    runtime.ExecuteAction(BattleAction.MoveUnit(player, [runtime.TryGetTile(new Vector3I(1, 0, 0)).RequireSome()]));
    ClickBody(battle, new Vector3(0.5f, 0.85f, 0.5f));
    Assert.Equal(0, Verbs(battle).GetChildCount(), "Playback gates selection while the body is moving.");
    await DrainDirector(battle);
    await WaitForPhysics(battle);
    clicked = None;
    ClickBody(battle, new Vector3(0.5f, 0.85f, 0.5f));
    Assert.True(clicked.IsNone, "The old body position must no longer intercept clicks.");
    ClickBody(battle, new Vector3(1.5f, 0.85f, 0.5f));
    Assert.Equal(player.State, clicked.RequireSome());
    Assert.True(Verbs(battle).GetChildCount() > 0, "The moved body's identity resolves its current tile.");
    runtime.ExecuteAction(BattleAction.ApplyDamage(runtime.TryGetAlive(player.State).RequireSome(), 100));
    await DrainDirector(battle);
    await WaitForPhysics(battle);
    clicked = None;
    ClickBody(battle, new Vector3(1.5f, 0.85f, 0.5f));
    Assert.True(clicked.IsNone, "Death playback hides the body and removes it from picking.");
  }
  [TestCase(1)]
  [TestCase(2)]
  public static async Task AuthoredPropsBlockBodyClicksWithoutSelectingTheTileBehind(int propItem)
  {
    await using var cleanup = new DeferredNodeCleanup();
    var (battle, _, _) = CreateBattle();
    var palette = GD.Load<MeshLibrary>("res://resources/map_entities/MapPalette.tres");
    var floor = new GridMap { MeshLibrary = palette, CellSize = Vector3.One, CollisionLayer = 1, CollisionMask = 0 };
    floor.SetCellItem(Godot.Vector3I.Zero, 0);
    battle.AddChild(floor);
    var props = new GridMap { MeshLibrary = palette, CellSize = Vector3.One, CollisionLayer = 2, CollisionMask = 0 };
    props.SetCellItem(new Godot.Vector3I(0, 0, 1), propItem);
    battle.AddChild(props);
    Camera(battle).GlobalPosition = new Vector3(0.5f, 2.5f, 5);
    Camera(battle).LookAt(new Vector3(0.5f, 0.5f, 0.5f));
    await WaitForPhysics(battle);
    Vector3 covered = new(0.5f, 0.1f, 0.5f);
    Vector2 pointer = Camera(battle).UnprojectPosition(covered);
    Vector3 floorHit = GameCamera.RaycastViewportPosition(Camera(battle), battle.GetWorld3D(), pointer)["position"].AsVector3();
    Assert.Equal(Vector3I.Zero, BoardCoordinates.WorldToTile(floorHit), "A floor fallback would select this same occluded unit.");
    ClickBody(battle, covered);
    Assert.Equal(0, Verbs(battle).GetChildCount(), "The real palette prop must consume the covered body click.");
    ClickBody(battle, new Vector3(0.5f, 0.95f, 0.5f));
    Assert.True(Verbs(battle).GetChildCount() > 0, "The visible upper body above the prop remains clickable.");
  }
  private static (BattleScene Battle, BattleRuntime Runtime, SubViewport Viewport) CreateBattle(BattleTypeData? type = null)
  {
    var setup = BattleSetupResolver.Resolve(type ?? TestData.MakeDuelBattleType()).RequireRight();
    var runtime = BattleFactory.Start(setup).RequireRight();
    var battle = CreateBattleScene();
    battle.Present(runtime, setup);
    var viewport = new SubViewport
    {
      Size = new Vector2I(800, 600),
      OwnWorld3D = true,
      RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
    };
    viewport.AddChild(battle);
    AddToTree(viewport);
    battle.InitializePresentation();
    var rig = battle.GetNode<GameCamera>("GameCamera");
    rig.SetProcess(false);
    rig.SetPhysicsProcess(false);
    var camera = Camera(battle);
    camera.GlobalPosition = new Vector3(1, 2.5f, 5);
    camera.LookAt(new Vector3(1, 0.5f, 0.5f));
    return (battle, runtime, viewport);
  }

  private static Camera3D Camera(BattleScene battle)
    => battle.GetNode<Camera3D>("GameCamera/CameraPath/PathFollow3D/Camera");

  private static Container Verbs(BattleScene battle)
    => battle.GetNode<BattleHud>("BattleUI/BattleHud").GetNode<Container>("%VerbButtons");

  private static Button AttackButton(BattleScene battle)
    => Verbs(battle).GetChildren().AsValueEnumerable().OfType<Button>()
      .Single(button => string.Equals(button.Text, "Attack", StringComparison.Ordinal));

  private static void ClickBody(BattleScene battle, Vector3 body, bool click = true)
  {
    Vector2 pointer = Camera(battle).UnprojectPosition(body);
    var viewport = battle.GetViewport();
    viewport.PushInput(new InputEventMouseMotion { Position = pointer, GlobalPosition = pointer }, true);
    if (!click) return;
    viewport.PushInput(new InputEventMouseButton { Position = pointer, ButtonIndex = MouseButton.Left, Pressed = true }, true);
    viewport.PushInput(new InputEventMouseButton { Position = pointer, ButtonIndex = MouseButton.Left, Pressed = false }, true);
  }

  private static async Task WaitForPhysics(Node node)
  {
    await node.ToSignal(node.GetTree(), SceneTree.SignalName.PhysicsFrame);
    await node.ToSignal(node.GetTree(), SceneTree.SignalName.PhysicsFrame);
  }
}
