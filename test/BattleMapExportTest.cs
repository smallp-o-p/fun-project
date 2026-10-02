using FunProject.Battle;
using GdUnit4;
using Godot;
using static GdUnit4.Assertions;
using Cell = Godot.Vector3I;

[TestSuite]
[RequireGodotRuntime]
public partial class BattleMapExportTest
{
  [TestCase]
  public void TypedAssetsRetainNestedVisualsAndFloorPickingThroughReload()
  {
    var source = AutoFree(new BattleMapAuthoring { GridOrigin = new(10, 2, 20), CellWidth = 2, LevelHeight = 3 })!;
    var footprint = new BattleFootprintData { Cells = { [Cell.Zero] = new() { HasFloor = true }, [new(1, 0, 0)] = new() { HasFloor = true } } };
    var piece = new BattleFloor { Name = "Floor", Footprint = footprint, Position = source.GridOrigin };
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
  public void VisualChangesDoNotChangeGameplayAndBothTypesUseTheSameFootprint()
  {
    var footprint = new BattleFootprintData { Cells = { [Cell.Zero] = new() { HasFloor = true, SpawnFactionSlot = 1 } } };
    var source = AutoFree(new BattleMapAuthoring())!;
    var piece = new BattleProp { Footprint = footprint };
    source.AddChild(piece);
    using var first = source.BuildScene();
    piece.AddChild(new MeshInstance3D { Mesh = new SphereMesh(), Scale = Vector3.One * 5 });
    using var second = source.BuildScene();
    source.RemoveChild(piece); piece.Free();
    source.AddChild(new BattleFloor { Footprint = footprint });
    using var third = source.BuildScene();
    foreach (var packed in new[] { first, second, third })
    {
      var map = AutoFree(packed.Instantiate<BattleMap>())!;
      Assert.True(map.MapData.Tiles[Cell.Zero].Walkable);
      Assert.Equal(1, map.MapData.Tiles[Cell.Zero].SpawnFactionSlot);
    }
  }
  [TestCase]
  public void ScaledMapRootIsRejectedBeforeExport()
  {
    var source = AutoFree(new BattleMapAuthoring { Scale = Vector3.One * 2 })!;
    source.AddChild(new BattleFloor { Footprint = TestData.MakeFloorFootprint(1, 1) });
    Assert.Throws<System.InvalidOperationException>(() => source.BuildScene());
  }

  [TestCase]
  public void SavedExampleAssetsCanReloadAndReexportWithoutDuplicatingChildren()
  {
    var source = AutoFree(GD.Load<PackedScene>("res://scenes/battle/authoring/BattleMapAuthoring.tscn").Instantiate<BattleMapAuthoring>())!;
    var car = source.GetNode<BattleProp>("Car");
    Assert.Equal(1, car.GetChildCount());
    Assert.Equal(2, car.Footprint.Cells.Count);
    var template = AutoFree(GD.Load<PackedScene>("res://scenes/battle/authoring/Car.tscn").Instantiate<BattleProp>())!;
    Assert.True(ReferenceEquals(template.Footprint, car.Footprint));
    using var packed = source.BuildScene();
    var baked = AutoFree(packed.Instantiate<BattleMap>())!;
    Assert.Equal(1, baked.GetNode<BattleProp>("Car").GetChildCount());
    Assert.Equal(2u, baked.GetNode<StaticBody3D>("Floor_0_0/Surface").CollisionLayer);
    Assert.False(baked.MapData.Tiles[new(2, 0, 2)].Walkable);
    Assert.False(baked.MapData.Tiles[new(2, 0, 3)].Walkable);
    Assert.Equal(40, baked.MapData.Tiles[new(2, 0, 1)].CoverAmount);
  }

  [TestCase]
  public async System.Threading.Tasks.Task PickingHitsExplicitRoofAndIgnoresHigherVisualCollider()
  {
    var source = AutoFree(new BattleMapAuthoring { GridOrigin = new(10, 2, 20), CellWidth = 2, LevelHeight = 3 })!;
    var prop = new BattleProp
    {
      Position = source.GridOrigin,
      Footprint = new BattleFootprintData
      {
        Cells = {
      [Cell.Zero] = new() { HasFloor = true, BlocksMovement = true, WalkableTop = true } }
      }
    };
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

  [TestCase(false)]
  [TestCase(true)]
  public void TransformBreaksMatchGodotVisualPlacement(bool topLevel)
  {
    var source = AutoFree(new BattleMapAuthoring())!;
    var group = new Node3D { Position = Vector3.Right * 2 };
    source.AddChild(group);
    Node parent = group;
    if (!topLevel) { parent = new Node(); group.AddChild(parent); }
    parent.AddChild(new BattleProp { TopLevel = topLevel, Footprint = TestData.MakeFloorFootprint(1, 1) });
    using var packed = source.BuildScene();
    var map = AutoFree(packed.Instantiate<BattleMap>())!;
    Assert.True(map.MapData.Tiles.ContainsKey(Cell.Zero));
  }

}
