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
  public void ExportPreservesNonzeroCellsAndVisualChildrenAfterReload()
  {
    var map = AutoFree(MakeMap())!;
    using var packed = map.BuildScene();
    string path = "user://battle_prop_roundtrip.tscn";
    Assert.Equal(Error.Ok, ResourceSaver.Save(packed, path));
    var reloaded = AutoFree(ResourceLoader.Load<PackedScene>(path, cacheMode: ResourceLoader.CacheMode.Ignore).Instantiate<BattleMap>())!;
    Assert.Equal(new Cell(4, 1, 5), reloaded.MapData.Dimensions);
    Assert.False(reloaded.MapData.Tiles[new(3, 0, 4)].Walkable);
    Assert.True(reloaded.GetNode("Car/Body") is MeshInstance3D);
    Assert.True(reloaded.GetNode("Car") is not BattlePropAuthoring);
  }

  [TestCase]
  public void SnappingUsesAuthoredGroundSurfaceAndQuarterTurns()
  {
    var map = AutoFree(MakeMap())!;
    var prop = map.GetNode<BattlePropAuthoring>("Car");
    prop.Position = new(3.7f, 0.2f, 4.4f);
    prop.Rotation = new(0, 1.6f, 0);
    prop.SnapToGrid();
    Assert.Equal(new Cell(3, 0, 4), prop.Anchor);
    Assert.Equal(1, prop.QuarterTurns);
    Assert.True(prop.Position.IsEqualApprox(new(3.5f, 0.05f, 4.5f)));
  }

  [TestCase]
  public void ExportRejectsUnsnappedOrScaledVisuals()
  {
    var map = AutoFree(MakeMap())!;
    var prop = map.GetNode<BattlePropAuthoring>("Car");
    prop.Scale = new(2, 1, 1);
    Assert.Throws<InvalidOperationException>(() => map.BuildScene());
  }

  [TestCase]
  public void SavedExampleSourceCanBeReexportedWithItsVisuals()
  {
    var authoring = AutoFree(ResourceLoader.Load<PackedScene>("res://scenes/battle/authoring/PropExample.tscn").Instantiate<BattleMapAuthoring>())!;
    using var packed = authoring.BuildScene();
    var reloaded = AutoFree(packed.Instantiate<BattleMap>())!;
    Assert.True(reloaded.HasNode("ExampleCar/Body"));
    Assert.False(reloaded.MapData.Tiles[new(3, 0, 3)].Walkable);
  }

  [TestCase]
  public void SnapKeepsTheNearestAuthoredSurfaceWithNegativeOffset()
  {
    var map = AutoFree(MakeMap())!;
    map.Palette!.Brushes["Floor"].GroundSurfaceOffset = -0.05f;
    map.SetCellItem(new(3, 1, 4), 0);
    var prop = map.GetNode<BattlePropAuthoring>("Car");
    prop.Anchor = new(3, 1, 4);
    prop.ApplyPlacement();
    prop.SnapToGrid();
    Assert.Equal(new Cell(3, 1, 4), prop.Anchor);
    Assert.True(prop.Position.IsEqualApprox(new(3.5f, 0.95f, 4.5f)));
  }

  [TestCase]
  public void ExportMovesEverySupportedVisualColliderOffTheGroundLayer()
  {
    var map = AutoFree(MakeMap())!;
    var prop = map.GetNode<BattlePropAuthoring>("Car");
    prop.AddChild(new CsgBox3D { Name = "Csg", UseCollision = true });
    prop.AddChild(new GridMap { Name = "Grid", CollisionLayer = 1 });
    prop.AddChild(new StaticBody3D { Name = "BodyCollider", CollisionLayer = 1 });
    using var packed = map.BuildScene();
    var baked = AutoFree(packed.Instantiate<BattleMap>())!;
    Assert.Equal(2u, baked.GetNode<CsgBox3D>("Car/Csg").CollisionLayer);
    Assert.Equal(2u, baked.GetNode<GridMap>("Car/Grid").CollisionLayer);
    Assert.Equal(2u, baked.GetNode<StaticBody3D>("Car/BodyCollider").CollisionLayer);
  }

  private static BattleMapAuthoring MakeMap()
  {
    var library = new MeshLibrary();
    library.CreateItem(0);
    library.SetItemName(0, "Floor");
    library.SetItemMesh(0, new BoxMesh());
    var palette = new BattleTilePalette { MeshLibrary = library };
    palette.Brushes["Floor"] = new BattleMapTileData { GroundSurfaceOffset = 0.05f };
    var map = new BattleMapAuthoring { Palette = palette, MeshLibrary = library, CellSize = Vector3.One, CellCenterY = false };
    map.SetCellItem(new(3, 0, 4), 0);
    var prop = new BattlePropAuthoring { Name = "Car", Definition = new BattlePropData { Footprint = [Cell.Zero], BlocksMovement = true }, Anchor = new(3, 0, 4) };
    map.AddChild(prop);
    prop.AddChild(new MeshInstance3D { Name = "Body", Mesh = new BoxMesh() });
    prop.ApplyPlacement();
    return map;
  }
}
