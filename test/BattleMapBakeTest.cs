using GdUnit4;
using Godot;
using static GdUnit4.Assertions;

[TestSuite]
[RequireGodotRuntime]
public partial class BattleMapBakeTest
{
  [TestCase]
  public void AssetsRetainNestedVisualsAndFloorPickingThroughReload()
  {
    var source = AutoFree(new BattleMapAuthoring())!;
    var footprint = TestData.MakeFloorFootprint(2, 1);
    source.SpawnSlots[Godot.Vector3I.Zero] = 1;
    var piece = new Node3D();
    TestData.Annotate(piece, footprint);
    piece.Name = "Floor";
    source.AddChild(piece); piece.Owner = source;
    var nested = new Node3D { Name = "Nested", Scale = new(2, 3, 4) };
    piece.AddChild(nested); nested.Owner = source;
    var visual = new MeshInstance3D { Name = "Visual", Mesh = new BoxMesh() };
    nested.AddChild(visual); visual.Owner = source;
    var collision = new StaticBody3D { Name = "ArtCollision", CollisionLayer = 1 };
    nested.AddChild(collision); collision.Owner = source;
    using var packed = TestData.BakeMapScene(source);
    const string path = "user://map_bake_roundtrip.tscn";
    Assert.Equal(Error.Ok, ResourceSaver.Save(packed, path));
    var map = AutoFree(ResourceLoader.Load<PackedScene>(path, cacheMode: ResourceLoader.CacheMode.Ignore).Instantiate<BattleMap>())!;
    Assert.Equal(new Godot.Vector3I(2, 1, 1), map.MapData.Dimensions);
    Assert.True(map.MapData.Tiles[Godot.Vector3I.Zero].Walkable);
    Assert.True(map.MapData.Tiles[new(1, 0, 0)].Walkable);
    Assert.Equal(1, map.MapData.Tiles[Godot.Vector3I.Zero].SpawnFactionSlot);
    Assert.Equal(new Vector3(2, 3, 4), map.GetNode<Node3D>("Floor/Nested").Scale);
    Assert.True(map.GetNode<MeshInstance3D>("Floor/Nested/Visual").Mesh is BoxMesh);
    Assert.Equal(2u, map.GetNode<StaticBody3D>("Floor/Nested/ArtCollision").CollisionLayer);
    var picking = map.GetNode<StaticBody3D>("FloorPicking");
    Assert.Equal(1u, picking.CollisionLayer);
    Assert.Equal(2, picking.GetChildCount());
    Assert.True(picking.GetChild<CollisionShape3D>(0).Position.IsEqualApprox(new(0.5f, -0.01f, 0.5f)));
    Assert.Equal(2u, collision.CollisionLayer);
    Assert.True(footprint[Godot.Vector3I.Zero].HasFloor);
    using var again = TestData.BakeMapScene(source);
    Assert.Equal(2, AutoFree(again.Instantiate<BattleMap>())!.MapData.Tiles.Count);
  }

  [TestCase(0)]
  [TestCase(1)]
  [TestCase(2)]
  [TestCase(3)]
  public void MapRootMustUseTheFixedBoardConvention(int scenario)
  {
    var source = AutoFree(new BattleMapAuthoring())!;
    if (scenario == 0) source.Scale = Vector3.One * 2;
    if (scenario == 1) source.Position = Vector3.Right;
    if (scenario == 2) source.RotateY(Mathf.Pi / 2);
    if (scenario == 3)
    {
      var parent = AutoFree(new Node3D { Position = Vector3.Right })!;
      parent.AddChild(source);
      source.Position = Vector3.Left;
    }
    var floor = new Node3D();
    TestData.Annotate(floor, TestData.MakeFloorFootprint(1, 1));
    source.AddChild(floor);
    Assert.Throws<System.InvalidOperationException>(() => source.Bake());
  }

  [TestCase]
  public async System.Threading.Tasks.Task PickingHitsExplicitRoofAndIgnoresHigherVisualCollider()
  {
    var source = AutoFree(new BattleMapAuthoring())!;
    var prop = new Node3D();
    TestData.Annotate(prop, new Godot.Collections.Dictionary<Godot.Vector3I, BattleFootprintData> { [Godot.Vector3I.Zero] = new() { HasFloor = true, BlocksMovement = true, WalkableTop = true } });
    source.AddChild(prop);
    var art = new StaticBody3D { Position = new(0.5f, 8, 0.5f), CollisionLayer = 1 };
    prop.AddChild(art);
    art.AddChild(new CollisionShape3D { Shape = new BoxShape3D() });
    using var packed = TestData.BakeMapScene(source);
    var map = FunProject.Tests.GeoscapeTestScenes.AddToTree(packed.Instantiate<BattleMap>());
    var tree = (SceneTree)Engine.GetMainLoop();
    await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
    await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
    var camera = FunProject.Tests.GeoscapeTestScenes.AddToTree(new Camera3D { Position = new(0.5f, 20, 0.5f) });
    camera.LookAt(new(0.5f, 1, 0.5f), Vector3.Forward);
    var hit = GameCamera.TryRaycastViewportPosition(camera, map.GetWorld3D(), camera.GetViewport().GetVisibleRect().GetCenter()).RequireSome();
    Assert.True(hit.IsEqualApprox(new(0.5f, 1, 0.5f)));
    Assert.Equal(new Vector3I(0, 1, 0), BoardCoordinates.WorldToTile(hit));
  }

  [TestCase]
  public void BakeStoresDataAndPickingInTheEditableScene()
  {
    var source = AutoFree(new BattleMapAuthoring { Name = "Map" })!;
    var asset = AutoFree(new Node3D { Name = "Floor" })!;
    TestData.Annotate(asset, TestData.MakeFloorFootprint(2, 1));
    var collision = new StaticBody3D { Name = "ArtCollision", CollisionLayer = 1 };
    asset.AddChild(collision); collision.Owner = asset;
    using var assetScene = new PackedScene();
    Assert.Equal(Error.Ok, assetScene.Pack(asset));
    const string assetPath = "user://editable_bake_asset.tscn";
    Assert.Equal(Error.Ok, ResourceSaver.Save(assetScene, assetPath));
    var floor = ResourceLoader.Load<PackedScene>(assetPath, cacheMode: ResourceLoader.CacheMode.Ignore).Instantiate<Node3D>();
    source.AddChild(floor); floor.Owner = source;
    source.SpawnSlots[Godot.Vector3I.Right] = 1;
    source.Bake();
    source.Bake();
    Assert.Equal(2, source.MapData.Tiles.Count);
    Assert.Equal(2, source.GetNode("FloorPicking").GetChildCount());
    Assert.True(floor.HasNode("Annotations"));
    using var packed = new PackedScene();
    Assert.Equal(Error.Ok, packed.Pack(source));
    const string path = "user://editable_baked_map.tscn";
    Assert.Equal(Error.Ok, ResourceSaver.Save(packed, path));
    var restored = ResourceLoader.Load<PackedScene>(path, cacheMode: ResourceLoader.CacheMode.Ignore).Instantiate<BattleMapAuthoring>();
    Assert.Equal(1, restored.MapData.Tiles[Godot.Vector3I.Right].SpawnFactionSlot);
    Assert.Equal(1, restored.SpawnSlots[Godot.Vector3I.Right]);
    Assert.True(restored.GetNode("Floor").HasNode("Annotations"));
    Assert.Equal(2u, restored.GetNode<StaticBody3D>("Floor/ArtCollision").CollisionLayer);
    FunProject.Tests.GeoscapeTestScenes.AddToTree(restored);
    Assert.False(restored.GetNode("Floor").HasNode("Annotations"));
    Assert.Equal(2, restored.GetNode("FloorPicking").GetChildCount());
    Assert.Equal(1, restored.MapData.Tiles[Godot.Vector3I.Right].SpawnFactionSlot);
  }

  [TestCase]
  public void MapSpawnRequiresWalkableCell()
  {
    var source = AutoFree(new BattleMapAuthoring())!;
    var floor = new Node3D();
    source.AddChild(floor);
    TestData.Annotate(floor, TestData.MakeFloorFootprint(1, 1));
    source.SpawnSlots[Godot.Vector3I.Right] = 0;
    Assert.Throws<System.InvalidOperationException>(() => source.Bake());
  }

}
