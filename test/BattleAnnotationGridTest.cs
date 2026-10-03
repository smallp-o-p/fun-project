using GdUnit4;
using Godot;
using System;
using static GdUnit4.Assertions;
using Cell = Godot.Vector3I;

[TestSuite]
[RequireGodotRuntime]
public partial class BattleAnnotationGridTest
{
  [TestCase]
  public void PaintingAndOrientationChangesInvalidateSnapshotUntilRestored()
  {
    var grid = AutoFree(new BattleAnnotationGrid())!;
    grid.SetCellItem(Cell.Zero, 0);
    grid.Baseline = grid.CaptureLayout();
    Assert.True(grid.LayoutMatches());
    grid.SetCellItem(Cell.Right, 0);
    Assert.False(grid.LayoutMatches());
    grid.SetCellItem(Cell.Right, -1);
    Assert.True(grid.LayoutMatches());
    grid.SetCellItem(Cell.Zero, 0, 10);
    Assert.False(grid.LayoutMatches());
    grid.SetCellItem(Cell.Zero, 0);
    Assert.True(grid.LayoutMatches());
  }

  [TestCase]
  public void ExactlyOneOccupiedCellIsRequired()
  {
    var grid = AutoFree(new BattleAnnotationGrid())!;
    grid.SetCellItem(Cell.Zero, 0);
    Assert.False(grid.TrySelectedCell([], out _));
    Assert.False(grid.TrySelectedCell([Cell.Zero, Cell.Right], out _));
    Assert.False(grid.TrySelectedCell([Cell.Right], out _));
    Assert.True(grid.TrySelectedCell([Cell.Zero], out var cell));
    Assert.Equal(Cell.Zero, cell);
  }

  [TestCase]
  public void SavedAnnotationsAndLayoutRoundTripAndRejectLaterLayoutChange()
  {
    var root = AutoFree(new BattleProp { Name = "Asset" })!;
    TestData.Annotate(root, new BattleFootprintData
    {
      Cells = { [Cell.Zero] = new() { BlocksMovement = true, CoverAmount = 45 } }
    });
    using var packed = new PackedScene();
    Assert.Equal(Error.Ok, packed.Pack(root));
    const string path = "user://annotation_roundtrip.tscn";
    Assert.Equal(Error.Ok, ResourceSaver.Save(packed, path));
    var restored = AutoFree(ResourceLoader.Load<PackedScene>(path, cacheMode: ResourceLoader.CacheMode.Ignore).Instantiate<BattleProp>())!;
    var loaded = restored.GetNode<BattleAnnotationGrid>("Annotations");
    Assert.True(loaded.LayoutMatches());
    var footprint = loaded.BuildFootprint(Vector3.One);
    Assert.True(footprint.Cells[Cell.Zero].BlocksMovement);
    Assert.Equal(45, footprint.Cells[Cell.Zero].CoverAmount);
    loaded.SetCellItem(Cell.Right, 0);
    Assert.Throws<InvalidOperationException>(() => loaded.BuildFootprint(Vector3.One));
  }
  [TestCase(false)]
  [TestCase(true)]
  public void AnnotationExportUsesOneFootprintStripsMarkersAndKeepsModel(bool solid)
  {
    var source = AutoFree(new BattleMapAuthoring())!;
    var floor = new BattleFloor { Name = "Floor" };
    TestData.Annotate(floor, TestData.MakeFloorFootprint(4, 3));
    source.AddChild(floor); floor.Owner = source;
    var prop = new BattleProp { Name = "Prop", Position = new(1, 0, 1) };
    source.AddChild(prop); prop.Owner = source;
    var art = new MeshInstance3D { Name = "Model", Mesh = new BoxMesh() };
    prop.AddChild(art); art.Owner = source;
    var footprint = new BattleFootprintData();
    foreach (var cell in new Cell[] { Cell.Zero, Cell.Right })
      footprint.Cells[cell] = new() { BlocksMovement = solid, CoverDirections = FunProject.Battle.CoverDirections.North | FunProject.Battle.CoverDirections.East | FunProject.Battle.CoverDirections.South | FunProject.Battle.CoverDirections.West, CoverAmount = 40 };
    var grid = TestData.Annotate(prop, footprint);
    grid.Owner = source;
    using var packed = source.BuildScene();
    var result = AutoFree(packed.Instantiate<BattleMap>())!;
    Assert.False(result.GetNode("Prop").HasNode("Annotations"));
    Assert.True(result.GetNode("Prop").HasNode("Model"));
    Assert.Equal(!solid, result.MapData.Tiles[new(1, 0, 1)].Walkable);
    Assert.Equal(!solid, result.MapData.Tiles[new(2, 0, 1)].Walkable);
    Assert.Equal(40, result.MapData.Tiles[new(1, 0, 0)].CoverAmount);
    Assert.Equal(0, result.MapData.Tiles[new(2, 0, 1)].CoverAmount);
    Assert.True(prop.HasNode("Annotations"));
    grid.SetCellItem(Cell.Back, 0);
    Assert.Throws<InvalidOperationException>(() => source.BuildScene());
  }
  [TestCase(0)]
  [TestCase(1)]
  [TestCase(3)]
  public void AmbiguousOrMisplacedAnnotationSourcesAreRejected(int scenario)
  {
    var map = AutoFree(new BattleMapAuthoring())!;
    var floor = new BattleFloor { Position = Vector3.Right * 2 };
    TestData.Annotate(floor, TestData.MakeFloorFootprint(1, 1));
    map.AddChild(floor);
    var prop = new BattleProp(); map.AddChild(prop);
    Node3D parent = prop;
    if (scenario == 0) { parent = new Node3D(); prop.AddChild(parent); }
    TestData.Annotate(parent, TestData.MakeFloorFootprint(1, 1));
    if (scenario == 1) prop.AddChild(new BattleAnnotationGrid());
    if (scenario == 3)
    {
      var named = new Node3D { Name = "SceneUniqueVisual" };
      prop.AddChild(named); named.Owner = prop; named.UniqueNameInOwner = true;
    }
    Assert.Throws<InvalidOperationException>(() => map.BuildScene());
  }
}
