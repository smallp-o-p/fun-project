using FunProject.Battle;
using GdUnit4;
using Godot;
using System;
using static GdUnit4.Assertions;
using Cell = Godot.Vector3I;

[TestSuite]
[RequireGodotRuntime]
public partial class BattlePropAuthoringTest
{
  [TestCase]
  public void ExportPreservesCellsVisualOwnershipAndColliderLayersAfterReload()
  {
    var map = AutoFree(MakeMap())!;
    var prop = map.GetNode<BattlePropAuthoring>("Car");
    prop.AddChild(new CsgBox3D { Name = "Csg", UseCollision = true });
    prop.AddChild(new GridMap { Name = "Grid", CollisionLayer = 1 });
    prop.AddChild(new StaticBody3D { Name = "BodyCollider", CollisionLayer = 1 });
    using var packed = map.BuildScene();
    string path = "user://battle_prop_roundtrip.tscn";
    Assert.Equal(Error.Ok, ResourceSaver.Save(packed, path));
    var reloaded = AutoFree(ResourceLoader.Load<PackedScene>(path, cacheMode: ResourceLoader.CacheMode.Ignore).Instantiate<BattleMap>())!;
    Assert.Equal(new Cell(4, 1, 5), reloaded.MapData.Dimensions);
    Assert.False(reloaded.MapData.Tiles[new(3, 0, 4)].Walkable);
    Assert.True(reloaded.GetNode("Car/Body") is MeshInstance3D);
    Assert.True(reloaded.GetNode("Car") is not BattlePropAuthoring);
    Assert.Equal(2u, reloaded.GetNode<CsgBox3D>("Car/Csg").CollisionLayer);
    Assert.Equal(2u, reloaded.GetNode<GridMap>("Car/Grid").CollisionLayer);
    Assert.Equal(2u, reloaded.GetNode<StaticBody3D>("Car/BodyCollider").CollisionLayer);
  }

  [TestCase(0.05f, 0.2f, 0.05f)]
  [TestCase(-0.05f, 0.95f, 0.95f)]
  public void SnappingUsesNearestAuthoredSurfaceAndQuarterTurns(float offset, float inputY, float expectedY)
  {
    var map = AutoFree(MakeMap())!;
    map.Palette!.Brushes["0"].GroundSurfaceOffset = offset;
    map.SetCellItem(new(3, 1, 4), 0);
    var prop = map.GetNode<BattlePropAuthoring>("Car");
    prop.Position = new(3.7f, inputY, 4.4f);
    prop.Rotation = new(0, 1.6f, 0);
    prop.SnapToGrid();

    Assert.True(Mathf.IsEqualApprox(Mathf.Pi / 2, prop.Rotation.Y));
    Assert.True(prop.Position.IsEqualApprox(new(3.5f, expectedY, 4.5f)));
  }

  [TestCase(0.1f, 0f, 1f)]
  [TestCase(0f, 0.1f, 1f)]
  [TestCase(0f, 0f, 2f)]
  public void ExportRejectsUnsnappedOrScaledVisuals(float offset, float yaw, float scale)
  {
    var map = AutoFree(MakeMap())!;
    var prop = map.GetNode<BattlePropAuthoring>("Car");
    prop.Position += Vector3.Right * offset;
    prop.Rotation = new(0, yaw, 0);
    prop.Scale = new(scale, 1, 1);
    Assert.Throws<InvalidOperationException>(() => map.BuildScene());
  }

  [TestCase]
  public void SavedExampleSourceCanBeReexportedWithItsVisuals()
  {
    var authoring = AutoFree(ResourceLoader.Load<PackedScene>("res://scenes/battle/authoring/PropExample.tscn").Instantiate<BattleMapAuthoring>())!;
    using var packed = authoring.BuildScene();
    var reloaded = AutoFree(packed.Instantiate<BattleMap>())!;
    Assert.True(reloaded.HasNode("ExampleCar/Body"));
    Assert.False(reloaded.MapData.Tiles[new(2, 0, 2)].Walkable);
  }

  private static BattleMapAuthoring MakeMap()
  {
    var map = TestData.MakeMapAuthoring(new() { [new(3, 0, 4)] = new BattleMapTileData { GroundSurfaceOffset = 0.05f } });
    var prop = new BattlePropAuthoring { Name = "Car", BlocksMovement = true, Position = new(3.5f, 0.05f, 4.5f) };
    map.AddChild(prop);
    prop.AddChild(new MeshInstance3D { Name = "Body", Mesh = new BoxMesh() });
    return map;
  }
}
