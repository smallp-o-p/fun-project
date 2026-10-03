using GdUnit4;
using Godot;
using static GdUnit4.Assertions;
using Cell = Godot.Vector3I;

[TestSuite]
[RequireGodotRuntime]
public partial class BattleMapExportTest
{
  [TestCase(false)]
  [TestCase(true)]
  public void TypedAssetsRetainNestedVisualsAndFloorPickingThroughReload(bool isProp)
  {
    var source = AutoFree(new BattleMapAuthoring { GridOrigin = new(10, 2, 20), CellWidth = 2, LevelHeight = 3 })!;
    var footprint = TestData.MakeFloorFootprint(2, 1);
    footprint.Cells[Cell.Zero].SpawnFactionSlot = 1;
    Node3D piece = isProp ? new BattleProp() : new BattleFloor();
    TestData.Annotate(piece, footprint, new(2, 3, 2));
    piece.Name = "Floor";
    piece.Position = source.GridOrigin;
    source.AddChild(piece); piece.Owner = source;
    var nested = new Node3D { Name = "Nested", Scale = new(2, 3, 4) };
    piece.AddChild(nested); nested.Owner = source;
    var visual = new MeshInstance3D { Name = "Visual", Mesh = new BoxMesh() };
    nested.AddChild(visual); visual.Owner = source;
    var collision = new StaticBody3D { Name = "ArtCollision", CollisionLayer = 1 };
    nested.AddChild(collision); collision.Owner = source;
    using var packed = source.BuildScene();
    const string path = "user://map_export_roundtrip.tscn";
    Assert.Equal(Error.Ok, ResourceSaver.Save(packed, path));
    var map = AutoFree(ResourceLoader.Load<PackedScene>(path, cacheMode: ResourceLoader.CacheMode.Ignore).Instantiate<BattleMap>())!;
    Assert.Equal(new Cell(2, 1, 1), map.MapData.Dimensions);
    Assert.True(map.MapData.Tiles[Cell.Zero].Walkable);
    Assert.True(map.MapData.Tiles[new(1, 0, 0)].Walkable);
    Assert.Equal(1, map.MapData.Tiles[Cell.Zero].SpawnFactionSlot);
    Assert.Equal(new Vector3(2, 3, 4), map.GetNode<Node3D>("Floor/Nested").Scale);
    Assert.True(map.GetNode<MeshInstance3D>("Floor/Nested/Visual").Mesh is BoxMesh);
    Assert.Equal(2u, map.GetNode<StaticBody3D>("Floor/Nested/ArtCollision").CollisionLayer);
    var picking = map.GetNode<StaticBody3D>("FloorPicking");
    Assert.Equal(1u, picking.CollisionLayer);
    Assert.Equal(2, picking.GetChildCount());
    Assert.True(picking.GetChild<CollisionShape3D>(0).Position.IsEqualApprox(new(11, 1.97f, 21)));
    Assert.Equal(1u, collision.CollisionLayer);
    Assert.True(footprint.Cells[Cell.Zero].HasFloor);
    using var again = source.BuildScene();
    Assert.Equal(2, AutoFree(again.Instantiate<BattleMap>())!.MapData.Tiles.Count);
  }

  [TestCase]
  public void ScaledMapRootIsRejectedBeforeExport()
  {
    var source = AutoFree(new BattleMapAuthoring { Scale = Vector3.One * 2 })!;
    var floor = new BattleFloor();
    TestData.Annotate(floor, TestData.MakeFloorFootprint(1, 1));
    source.AddChild(floor);
    Assert.Throws<System.InvalidOperationException>(() => source.BuildScene());
  }

  [TestCase]
  public async System.Threading.Tasks.Task PickingHitsExplicitRoofAndIgnoresHigherVisualCollider()
  {
    var source = AutoFree(new BattleMapAuthoring { GridOrigin = new(10, 2, 20), CellWidth = 2, LevelHeight = 3 })!;
    var prop = new BattleProp { Position = source.GridOrigin };
    TestData.Annotate(prop, new BattleFootprintData
    {
      Cells = { [Cell.Zero] = new() { HasFloor = true, BlocksMovement = true, WalkableTop = true } }
    }, new(2, 3, 2));
    source.AddChild(prop);
    var art = new StaticBody3D { Position = new(1, 8, 1), CollisionLayer = 1 };
    prop.AddChild(art);
    art.AddChild(new CollisionShape3D { Shape = new BoxShape3D() });
    using var packed = source.BuildScene();
    var map = FunProject.Tests.GeoscapeTestScenes.AddToTree(packed.Instantiate<BattleMap>());
    var tree = (SceneTree)Engine.GetMainLoop();
    await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
    await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
    var camera = FunProject.Tests.GeoscapeTestScenes.AddToTree(new Camera3D { Position = new(11, 20, 21) });
    camera.LookAt(new(11, 5, 21), Vector3.Forward);
    var hit = GameCamera.TryRaycastViewportPosition(camera, map.GetWorld3D(), camera.GetViewport().GetVisibleRect().GetCenter()).RequireSome();
    Assert.True(hit.IsEqualApprox(new(11, 5, 21)));
    Assert.Equal(new Vector3I(0, 1, 0), new BoardCoordinates(map.MapData, map.GlobalTransform).WorldToTile(hit));
  }

}
