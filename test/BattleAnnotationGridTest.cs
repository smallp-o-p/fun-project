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
  public void SceneReloadPreservesSerializedNativeMarkerLibraryAndMetrics()
  {
    var root = AutoFree(new BattleProp { Name = "Asset" })!;
    var grid = TestData.Annotate(root, TestData.MakeFloorFootprint(1, 1), new(2, 3, 2));
    grid.MeshLibrary.ResourceName = "Authored marker library";
    using var packed = new PackedScene();
    Assert.Equal(Error.Ok, packed.Pack(root));
    const string path = "user://annotation_native_properties.tscn";
    Assert.Equal(Error.Ok, ResourceSaver.Save(packed, path));
    var loaded = AutoFree(ResourceLoader.Load<PackedScene>(path, cacheMode: ResourceLoader.CacheMode.Ignore)
      .Instantiate<BattleProp>())!.GetNode<BattleAnnotationGrid>("Annotations");
    Assert.Equal(new Vector3(2, 3, 2), loaded.CellSize);
    Assert.Equal("Authored marker library", loaded.MeshLibrary.ResourceName);
    Assert.Equal(new Vector3(2, 3, 2) * 0.96f, ((BoxMesh)loaded.MeshLibrary.GetItemMesh(0)).Size);
    Assert.True(loaded.BuildFootprint(new(2, 3, 2)).Cells[Cell.Zero].HasFloor);
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
    var grid = new BattleAnnotationGrid { Name = "Annotations" };
    root.AddChild(grid); grid.Owner = root;
    grid.SetCellItem(Cell.Zero, 0);
    grid.Baseline = grid.CaptureLayout();
    grid.Annotations.Cells[Cell.Zero] = new() { BlocksMovement = true, CoverAmount = 45 };
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
    var grid = new BattleAnnotationGrid { Name = "Annotations" };
    prop.AddChild(grid); grid.Owner = source;
    var art = new MeshInstance3D { Name = "Model", Mesh = new BoxMesh() };
    prop.AddChild(art); art.Owner = source;
    foreach (var cell in new Cell[] { Cell.Zero, Cell.Right })
    {
      grid.SetCellItem(cell, 0);
      grid.Annotations.Cells[cell] = new() { BlocksMovement = solid, CoverDirections = FunProject.Battle.CoverDirections.North | FunProject.Battle.CoverDirections.East | FunProject.Battle.CoverDirections.South | FunProject.Battle.CoverDirections.West, CoverAmount = 40 };
    }
    grid.Baseline = grid.CaptureLayout();
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
  [TestCase]
  public void NestedInstanceMarkersStayRemovedAfterExportReload()
  {
    var asset = AutoFree(new BattleProp { Name = "Prop" })!;
    var grid = new BattleAnnotationGrid { Name = "Annotations" };
    asset.AddChild(grid); grid.Owner = asset;
    grid.SetCellItem(Cell.Zero, 0);
    grid.Baseline = grid.CaptureLayout();
    grid.Annotations.Cells[Cell.Zero] = new() { HasFloor = true };
    var model = AutoFree(new Node3D { Name = "Model" })!;
    var mesh = new MeshInstance3D { Name = "Mesh", Mesh = new BoxMesh() };
    model.AddChild(mesh); mesh.Owner = model;
    using var visualScene = new PackedScene();
    Assert.Equal(Error.Ok, visualScene.Pack(model));
    const string visualPath = "user://annotation_visual.tscn";
    Assert.Equal(Error.Ok, ResourceSaver.Save(visualScene, visualPath));
    var visual = ResourceLoader.Load<PackedScene>(visualPath, cacheMode: ResourceLoader.CacheMode.Ignore).Instantiate<Node3D>();
    visual.Scale = new(2, 3, 4);
    asset.AddChild(visual); visual.Owner = asset;
    using var assetScene = new PackedScene();
    Assert.Equal(Error.Ok, assetScene.Pack(asset));
    const string assetPath = "user://annotation_asset.tscn";
    Assert.Equal(Error.Ok, ResourceSaver.Save(assetScene, assetPath));
    var instance = ResourceLoader.Load<PackedScene>(assetPath, cacheMode: ResourceLoader.CacheMode.Ignore).Instantiate();
    var map = AutoFree(new BattleMapAuthoring())!;
    map.AddChild(instance); instance.Owner = map;
    using var baked = map.BuildScene();
    const string mapPath = "user://annotation_export.tscn";
    Assert.Equal(Error.Ok, ResourceSaver.Save(baked, mapPath));
    var loaded = AutoFree(ResourceLoader.Load<PackedScene>(mapPath, cacheMode: ResourceLoader.CacheMode.Ignore).Instantiate<BattleMap>())!;
    Assert.False(loaded.GetNode("Prop").HasNode("Annotations"));
    Assert.True(loaded.GetNode("Prop").HasNode("Model"));
    Assert.Equal(1, loaded.GetNode("Prop/Model").GetChildCount());
    Assert.Equal(new Vector3(2, 3, 4), loaded.GetNode<Node3D>("Prop/Model").Scale);
    Assert.True(loaded.MapData.Tiles[Cell.Zero].Walkable);
  }

  [TestCase(0)]
  [TestCase(1)]
  [TestCase(3)]
  public void AmbiguousOrMisplacedAnnotationSourcesAreRejected(int scenario)
  {
    var map = AutoFree(new BattleMapAuthoring())!;
    var prop = new BattleProp(); map.AddChild(prop);
    Node parent = prop;
    if (scenario == 0) { parent = new Node3D(); prop.AddChild(parent); }
    var grid = new BattleAnnotationGrid(); parent.AddChild(grid);
    grid.SetCellItem(Cell.Zero, 0); grid.Baseline = grid.CaptureLayout();
    grid.Annotations.Cells[Cell.Zero] = new() { HasFloor = true };
    if (scenario == 1) prop.AddChild(new BattleAnnotationGrid());
    if (scenario == 3)
    {
      var named = new Node3D { Name = "SceneUniqueVisual" };
      prop.AddChild(named); named.Owner = prop; named.UniqueNameInOwner = true;
    }
    Assert.Throws<InvalidOperationException>(() => map.BuildScene());
  }
}
