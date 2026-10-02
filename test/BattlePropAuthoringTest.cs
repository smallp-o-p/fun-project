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
  [TestCase(0)]
  [TestCase(1)]
  [TestCase(2)]
  [TestCase(3)]
  public void ExportPreservesPaintedPropsGridMapAfterReload(int turns)
  {
    var map = AutoFree(TestData.MakeMapAuthoring(TestData.MakeOpenBattleMap(5, 5).Tiles))!;
    TestData.PaintProp(map, new() { PropFootprint = [Cell.Zero, new(0, 0, 1)], PropBlocksMovement = true }, new(2, 0, 2), turns);
    var props = map.Props!;
    var item = props.GetCellItem(new(2, 0, 2));
    props.MeshLibrary.SetItemShapes(item, [new BoxShape3D(), Transform3D.Identity]);
    using var packed = map.BuildScene();
    const string path = "user://battle_prop_roundtrip.tscn";
    Assert.Equal(Error.Ok, ResourceSaver.Save(packed, path));
    var baked = AutoFree(ResourceLoader.Load<PackedScene>(path, cacheMode: ResourceLoader.CacheMode.Ignore).Instantiate<BattleMap>())!;
    var layer = baked.GetNode<GridMap>("Props");
    Assert.Equal(new Cell(5, 1, 5), baked.MapData.Dimensions);
    Assert.False(baked.MapData.Tiles[new(2, 0, 2)].Walkable);
    Assert.Equal(25, baked.GetUsedCells().Count);
    Assert.Equal(1, layer.GetUsedCells().Count);
    Assert.Equal(item, layer.GetCellItem(new(2, 0, 2)));
    Assert.Equal(props.GetCellItemOrientation(new(2, 0, 2)), layer.GetCellItemOrientation(new(2, 0, 2)));
    Assert.Equal(2u, layer.CollisionLayer);
    Assert.Equal(2, layer.MeshLibrary.GetItemShapes(item).Count);
    Assert.Equal(1u, props.CollisionLayer);
  }

  [TestCase(0, TestName = "Props cannot tilt")]
  [TestCase(1, TestName = "Props grid cannot scale")]
  [TestCase(2, TestName = "Props grid cannot shift horizontally")]
  [TestCase(3, TestName = "Props height must match ground")]
  [TestCase(4, TestName = "Props cell scale must be one")]
  [TestCase(5, TestName = "Props grid must share the palette library")]
  public void ExportRejectsUnsupportedPaintedTransforms(int scenario)
  {
    var map = AutoFree(TestData.MakeMapAuthoring(new() { [Cell.Zero] = new BattleMapTileData() }))!;
    TestData.PaintProp(map, new() { PropFootprint = [Cell.Zero] }, Cell.Zero);
    var props = map.Props!;
    switch (scenario)
    {
      case 0: props.SetCellItem(Cell.Zero, props.GetCellItem(Cell.Zero), props.GetOrthogonalIndexFromBasis(new Basis(Vector3.Right, Mathf.Pi / 2))); break;
      case 1: props.Scale = new(2, 1, 1); break;
      case 2: props.Position = Vector3.Right * 0.1f; break;
      case 3: props.Position = Vector3.Up * 0.05f; break;
      case 4: props.CellScale = 2; break;
      case 5: props.MeshLibrary = new MeshLibrary(); break;
    }
    Assert.Throws<InvalidOperationException>(() => map.BuildScene());
  }

  [TestCase]
  public void SavedExampleSourceCanBeReexportedWithItsVisuals()
  {
    var source = ResourceLoader.Load<PackedScene>("res://scenes/battle/authoring/PropExample.tscn");
    var authoring = AutoFree(source.Instantiate<BattleMapAuthoring>())!;
    using var packed = authoring.BuildScene();
    var baked = AutoFree(packed.Instantiate<BattleMap>())!;
    Assert.Equal(new Vector3(0, 0.05f, 0), baked.GetNode<GridMap>("Props").Position);
    Assert.False(baked.MapData.Tiles[new(2, 0, 2)].Walkable);
    Assert.False(baked.MapData.Tiles[new(2, 0, 3)].Walkable);
    Assert.True(authoring.Palette!.Brushes["DebugFloor"].Walkable);
    Assert.Equal(40, authoring.Palette.Brushes["ExampleCar"].PropCoverNorth);
    Assert.Equal(0, authoring.Palette.Brushes["ExampleCar"].CoverNorth);
    using var again = authoring.BuildScene();
    var second = AutoFree(again.Instantiate<BattleMap>())!;
    Assert.Equal(40, second.MapData.Tiles[new(2, 0, 1)].CoverSouth);
  }
}
