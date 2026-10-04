using GdUnit4;
using Godot;
using System;
using static GdUnit4.Assertions;

[TestSuite]
[RequireGodotRuntime]
public partial class BattleAnnotationGridTest
{
  [TestCase]
  public void PaletteMarkerDrawsOnlyTheTwelveBoxEdges()
  {
    var grid = AutoFree(new BattleAnnotationGrid())!;
    AssertOutlineMarker(grid.MeshLibrary.GetItemMesh(0));
  }

  [TestCase]
  public void PaletteAddsFloorAndSolidOutlines()
  {
    var grid = AutoFree(new BattleAnnotationGrid())!;
    Assert.Equal(3, grid.MeshLibrary.GetItemList().Length);
    Assert.Equal("Floor", grid.MeshLibrary.GetItemName(1));
    Assert.Equal("Solid", grid.MeshLibrary.GetItemName(2));
    AssertOutlineMarker(grid.MeshLibrary.GetItemMesh(2), new Color(1, 0.55f, 0.15f, 1));

    var mesh = grid.MeshLibrary.GetItemMesh(1);
    Assert.True(mesh is ArrayMesh);
    var marker = (ArrayMesh)mesh;
    Assert.Equal(1, marker.GetSurfaceCount());
    Assert.Equal(Mesh.PrimitiveType.Lines, marker.SurfaceGetPrimitiveType(0));
    Assert.Equal(0, marker.SurfaceGetArrayIndexLen(0));
    var vertices = marker.SurfaceGetArrays(0)[(int)Mesh.ArrayType.Vertex].AsVector3Array();
    Assert.Equal(8, vertices.Length);
    var edges = new SysColGeneric.HashSet<(int, int)>();
    for (int i = 0; i < vertices.Length; i += 2)
    {
      int start = Corner(vertices[i]), end = Corner(vertices[i + 1]);
      Assert.True((start ^ end) is 1 or 2, "Each line must be a square edge, never a diagonal.");
      Assert.True(edges.Add((Math.Min(start, end), Math.Max(start, end))));
    }
    Assert.Equal(4, edges.Count);
    AssertMarkerMaterial(marker, new Color(0.3f, 0.9f, 0.35f, 1));

    static int Corner(Vector3 vertex)
    {
      Assert.True(Mathf.IsEqualApprox(vertex.Y, -0.5f), "The floor outline sits at the cell bottom.");
      Assert.True(Mathf.IsEqualApprox(Mathf.Abs(vertex.X), 0.48f));
      Assert.True(Mathf.IsEqualApprox(Mathf.Abs(vertex.Z), 0.48f));
      return (vertex.X > 0 ? 1 : 0) | (vertex.Z > 0 ? 2 : 0);
    }
  }

  private static void AssertOutlineMarker(Mesh mesh, Color? color = null)
  {
    Assert.True(mesh is ArrayMesh, "Annotation markers must have edge geometry without filled faces.");
    var marker = (ArrayMesh)mesh;
    Assert.Equal(1, marker.GetSurfaceCount());
    Assert.Equal(Mesh.PrimitiveType.Lines, marker.SurfaceGetPrimitiveType(0));
    Assert.Equal(0, marker.SurfaceGetArrayIndexLen(0));
    var vertices = marker.SurfaceGetArrays(0)[(int)Mesh.ArrayType.Vertex].AsVector3Array();
    Assert.Equal(24, vertices.Length);
    var edges = new SysColGeneric.HashSet<(int, int)>();
    for (int i = 0; i < vertices.Length; i += 2)
    {
      int start = Corner(vertices[i]), end = Corner(vertices[i + 1]);
      int changedAxes = start ^ end;
      Assert.True(changedAxes is 1 or 2 or 4, "Each line must be one box edge, never a face diagonal.");
      Assert.True(edges.Add((Math.Min(start, end), Math.Max(start, end))), "Box edges must not repeat.");
    }
    Assert.Equal(12, edges.Count);
    AssertMarkerMaterial(marker, color ?? new Color(0.15f, 0.85f, 0.75f, 1));

    static int Corner(Vector3 vertex)
    {
      Assert.True(vertex.Abs().IsEqualApprox(Vector3.One * 0.48f), "Edges must meet at the inset box corners.");
      return (vertex.X > 0 ? 1 : 0) | (vertex.Y > 0 ? 2 : 0) | (vertex.Z > 0 ? 4 : 0);
    }
  }

  private static void AssertMarkerMaterial(ArrayMesh marker, Color color)
  {
    Assert.True(marker.SurfaceGetMaterial(0) is StandardMaterial3D);
    var material = (StandardMaterial3D)marker.SurfaceGetMaterial(0);
    Assert.Equal(color, material.AlbedoColor);
    Assert.Equal(BaseMaterial3D.TransparencyEnum.Disabled, material.Transparency);
    Assert.Equal(BaseMaterial3D.ShadingModeEnum.Unshaded, material.ShadingMode);
    Assert.True(material.NoDepthTest);
  }

  [TestCase(0)]
  [TestCase(1)]
  [TestCase(2)]
  public void PaintedItemSuppliesOnlyItsDefaultsAndEachCellGetsFreshFlags(int item)
  {
    var root = AutoFree(new Node3D())!;
    var grid = TestData.Annotate(root, []);
    grid.SetCellItem(Godot.Vector3I.Zero, item);
    grid.SetCellItem(Godot.Vector3I.Right, item);
    if (item == 0) grid.Baseline = grid.CaptureLayout();
    var footprint = grid.BuildFootprint();
    var first = footprint[Godot.Vector3I.Zero];
    var second = footprint[Godot.Vector3I.Right];
    Assert.Equal(item == 1, first.HasFloor);
    Assert.Equal(item == 2, first.BlocksMovement);
    Assert.Equal(item == 2, first.BlocksLineOfSight);
    Assert.False(first.BlocksVerticalLineOfSight);
    Assert.False(first.WalkableTop);
    Assert.False(first.TopBlocksVerticalLineOfSight);
    Assert.Equal(FunProject.Battle.CoverDirections.None, first.CoverDirections);
    Assert.Equal(0, first.CoverAmount);
    Assert.False(first == second);
    first.HasFloor = !first.HasFloor;
    Assert.Equal(item == 1, second.HasFloor);
    Assert.Equal(item == 1, grid.BuildFootprint()[Godot.Vector3I.Zero].HasFloor);
    Assert.Equal(0, grid.Annotations.Count);
  }

  [TestCase(1)]
  [TestCase(2)]
  public void ExplicitCellFlagsReplaceShortcutDefaultsIncludingFalse(int item)
  {
    var root = AutoFree(new Node3D())!;
    var flags = new BattleFootprintData { CoverDirections = FunProject.Battle.CoverDirections.North, CoverAmount = 25 };
    var grid = TestData.Annotate(root, new() { [Godot.Vector3I.Zero] = flags });
    grid.SetCellItem(Godot.Vector3I.Zero, item);
    grid.Baseline = grid.CaptureLayout();
    var actual = grid.BuildFootprint()[Godot.Vector3I.Zero];
    Assert.True(actual == flags);
    Assert.False(actual.HasFloor);
    Assert.False(actual.BlocksMovement);
    Assert.False(actual.BlocksLineOfSight);
    Assert.Equal(FunProject.Battle.CoverDirections.North, actual.CoverDirections);
    Assert.Equal(25, actual.CoverAmount);
  }

  [TestCase]
  public void PaintedShortcutsBakeWithoutInspectionOrLockingTheLayout()
  {
    var map = AutoFree(new BattleMapAuthoring())!;
    var floor = new Node3D();
    map.AddChild(floor);
    var floorGrid = TestData.Annotate(floor, []);
    floorGrid.SetCellItem(Godot.Vector3I.Zero, 1);
    floorGrid.SetCellItem(Godot.Vector3I.Right, 1);
    var solid = new Node3D { Position = Vector3.Right };
    map.AddChild(solid);
    var solidGrid = TestData.Annotate(solid, []);
    solidGrid.SetCellItem(Godot.Vector3I.Zero, 2);

    map.Bake();

    Assert.Equal(2, map.MapData.Tiles.Count);
    Assert.True(map.MapData.Tiles[Godot.Vector3I.Zero].Walkable);
    Assert.False(map.MapData.Tiles[Godot.Vector3I.Zero].BlocksLineOfSight);
    Assert.False(map.MapData.Tiles[Godot.Vector3I.Right].Walkable);
    Assert.True(map.MapData.Tiles[Godot.Vector3I.Right].BlocksLineOfSight);
    Assert.Equal(0, map.MapData.Tiles[Godot.Vector3I.Right].CoverAmount);
    Assert.False(map.MapData.Tiles[Godot.Vector3I.Right].BlocksVerticalLineOfSight);
    Assert.Equal(2, map.GetNode("FloorPicking").GetChildCount());
    Assert.Equal(0, floorGrid.Annotations.Count);
    Assert.Equal(0, floorGrid.Baseline.Count);
    Assert.Equal(0, solidGrid.Annotations.Count);
    Assert.Equal(0, solidGrid.Baseline.Count);
    floorGrid.SetCellItem(new(2, 0, 0), 1);
    map.Bake();
    Assert.True(map.MapData.Tiles[new(2, 0, 0)].Walkable);
  }

  [TestCase]
  public void ExplicitShortcutAnnotationLocksAllPaintedItemsUntilLayoutIsRestored()
  {
    var root = AutoFree(new Node3D())!;
    var grid = TestData.Annotate(root, []);
    grid.SetCellItem(Godot.Vector3I.Zero, 1);
    grid.SetCellItem(Godot.Vector3I.Right, 2);
    grid.Annotations[Godot.Vector3I.Zero] = grid.BuildFootprint()[Godot.Vector3I.Zero];
    grid.Baseline = grid.CaptureLayout();
    Assert.True(grid.Validate().IsNone);
    grid.SetCellItem(Godot.Vector3I.Right, 1);
    Assert.True(grid.Validate(requireAnnotations: false).IsSome);
    Assert.Throws<InvalidOperationException>(() => grid.BuildFootprint());
    grid.SetCellItem(Godot.Vector3I.Right, 2);
    Assert.True(grid.Validate().IsNone);
    Assert.True(grid.BuildFootprint()[Godot.Vector3I.Right].BlocksMovement);
  }

  [TestCase]
  public void PaintingAndOrientationChangesInvalidateSnapshotUntilRestored()
  {
    var grid = AutoFree(new BattleAnnotationGrid())!;
    grid.SetCellItem(Godot.Vector3I.Zero, 0);
    grid.Baseline = grid.CaptureLayout();
    Assert.True(grid.LayoutMatches());
    grid.SetCellItem(Godot.Vector3I.Right, 0);
    Assert.False(grid.LayoutMatches());
    grid.SetCellItem(Godot.Vector3I.Right, -1);
    Assert.True(grid.LayoutMatches());
    grid.SetCellItem(Godot.Vector3I.Zero, 0, 10);
    Assert.False(grid.LayoutMatches());
    grid.SetCellItem(Godot.Vector3I.Zero, 0);
    Assert.True(grid.LayoutMatches());
  }

  [TestCase(0)]
  [TestCase(1)]
  [TestCase(2)]
  public void ExactlyOneOccupiedCellIsRequired(int item)
  {
    var grid = AutoFree(new BattleAnnotationGrid())!;
    grid.SetCellItem(Godot.Vector3I.Zero, item);
    Assert.False(grid.TrySelectedCell([], out _));
    Assert.False(grid.TrySelectedCell([Godot.Vector3I.Zero, Godot.Vector3I.Right], out _));
    Assert.False(grid.TrySelectedCell([Godot.Vector3I.Right], out _));
    Assert.True(grid.TrySelectedCell([Godot.Vector3I.Zero], out var cell));
    Assert.Equal(Godot.Vector3I.Zero, cell);
  }

  [TestCase]
  public void BakingRequiresAnnotationsWhileEditingAllowsAnUnsnapshottedLayout()
  {
    var root = AutoFree(new Node3D())!;
    var grid = TestData.Annotate(root, new()
    {
      [Godot.Vector3I.Zero] = new() { HasFloor = true }
    });
    grid.Baseline = [];
    Assert.True(grid.Validate().IsSome);
    Assert.True(grid.Validate(requireAnnotations: false).IsNone);
    Assert.Throws<InvalidOperationException>(() => grid.BuildFootprint());

    grid.Baseline = grid.CaptureLayout();
    Assert.True(grid.Validate().IsNone);
    grid.SetCellItem(Godot.Vector3I.Right, 0);
    Assert.True(grid.Validate().IsSome);
    Assert.True(grid.Validate(requireAnnotations: false).IsSome);
  }

  [TestCase]
  public void SavedAnnotationsAndLayoutRoundTripAndRejectLaterLayoutChange()
  {
    var root = AutoFree(new Node3D { Name = "Asset" })!;
    TestData.Annotate(root, new()
    {
      [Godot.Vector3I.Zero] = new() { BlocksMovement = true, CoverAmount = 45 }
    });
    using var packed = new PackedScene();
    Assert.Equal(Error.Ok, packed.Pack(root));
    const string path = "user://annotation_roundtrip.tscn";
    Assert.Equal(Error.Ok, ResourceSaver.Save(packed, path));
    var restored = AutoFree(ResourceLoader.Load<PackedScene>(path, cacheMode: ResourceLoader.CacheMode.Ignore).Instantiate<Node3D>())!;
    var loaded = restored.GetNode<BattleAnnotationGrid>("Annotations");
    AssertOutlineMarker(loaded.MeshLibrary.GetItemMesh(0));
    Assert.True(loaded.LayoutMatches());
    var footprint = loaded.BuildFootprint();
    Assert.True(footprint[Godot.Vector3I.Zero].BlocksMovement);
    Assert.Equal(45, footprint[Godot.Vector3I.Zero].CoverAmount);
    loaded.SetCellItem(Godot.Vector3I.Right, 0);
    Assert.Throws<InvalidOperationException>(() => loaded.BuildFootprint());
  }

  [TestCase(false)]
  [TestCase(true)]
  public void BakedMapUsesOneFootprintStripsRuntimeMarkersAndKeepsModel(bool solid)
  {
    var source = AutoFree(new BattleMapAuthoring())!;
    var floor = new Node3D { Name = "Floor" };
    TestData.Annotate(floor, TestData.MakeFloorFootprint(4, 3));
    source.AddChild(floor); floor.Owner = source;
    var prop = new Node3D { Name = "Prop", Position = new(1, 0, 1) };
    source.AddChild(prop); prop.Owner = source;
    var art = new MeshInstance3D { Name = "Model", Mesh = new BoxMesh() };
    prop.AddChild(art); art.Owner = source;
    Godot.Collections.Dictionary<Godot.Vector3I, BattleFootprintData> footprint = [];
    foreach (var cell in new Godot.Vector3I[] { Godot.Vector3I.Zero, Godot.Vector3I.Right })
      footprint[cell] = new() { BlocksMovement = solid, CoverDirections = FunProject.Battle.CoverDirections.North | FunProject.Battle.CoverDirections.East | FunProject.Battle.CoverDirections.South | FunProject.Battle.CoverDirections.West, CoverAmount = 40 };
    var grid = TestData.Annotate(prop, footprint);
    grid.Owner = source;
    using var packed = TestData.BakeMapScene(source);
    var result = FunProject.Tests.GeoscapeTestScenes.AddToTree(packed.Instantiate<BattleMap>());
    Assert.False(result.GetNode("Prop").HasNode("Annotations"));
    Assert.True(result.GetNode("Prop").HasNode("Model"));
    Assert.Equal(!solid, result.MapData.Tiles[new(1, 0, 1)].Walkable);
    Assert.Equal(!solid, result.MapData.Tiles[new(2, 0, 1)].Walkable);
    Assert.Equal(40, result.MapData.Tiles[new(1, 0, 0)].CoverAmount);
    Assert.Equal(0, result.MapData.Tiles[new(2, 0, 1)].CoverAmount);
    Assert.True(prop.HasNode("Annotations"));
    grid.SetCellItem(Godot.Vector3I.Back, 0);
    Assert.Throws<InvalidOperationException>(() => source.Bake());
  }

  [TestCase]
  public void NestedAnnotatedNodesUseTheirInheritedPlacement()
  {
    var map = AutoFree(new BattleMapAuthoring())!;
    var outer = new Node3D { Name = "Outer", Position = new(1, 0, 1) };
    map.AddChild(outer);
    TestData.Annotate(outer, TestData.MakeFloorFootprint(1, 1));
    var inner = new Node3D { Name = "Inner", Position = Vector3.Right };
    outer.AddChild(inner);
    TestData.Annotate(inner, TestData.MakeFloorFootprint(1, 1));

    using var packed = TestData.BakeMapScene(map);
    var result = FunProject.Tests.GeoscapeTestScenes.AddToTree(packed.Instantiate<BattleMap>());
    Assert.Equal(2, result.MapData.Tiles.Count);
    Assert.True(result.MapData.Tiles[new(1, 0, 1)].Walkable);
    Assert.True(result.MapData.Tiles[new(2, 0, 1)].Walkable);
    Assert.False(result.GetNode("Outer").HasNode("Annotations"));
    Assert.False(result.GetNode("Outer/Inner").HasNode("Annotations"));
  }

  [TestCase]
  public void AnnotationsBelongToReusableAssetsRatherThanTheMapRoot()
  {
    var map = AutoFree(new BattleMapAuthoring())!;
    var grid = TestData.Annotate(map, TestData.MakeFloorFootprint(1, 1));
    Assert.True(grid.Validate(requireAnnotations: false).IsSome);
    Assert.Throws<InvalidOperationException>(() => map.Bake());
  }

  [TestCase]
  public void MultipleAnnotationGridsInOneAssetAreRejected()
  {
    var map = AutoFree(new BattleMapAuthoring())!;
    var asset = new Node3D();
    map.AddChild(asset);
    TestData.Annotate(asset, TestData.MakeFloorFootprint(1, 1));
    asset.AddChild(new BattleAnnotationGrid());
    Assert.Throws<InvalidOperationException>(() => map.Bake());
  }

  [TestCase]
  public void SceneUniqueVisualsKeepTheirIdentityDuringBake()
  {
    var map = AutoFree(new BattleMapAuthoring())!;
    var asset = new Node3D();
    map.AddChild(asset);
    TestData.Annotate(asset, TestData.MakeFloorFootprint(1, 1));
    var named = new Node3D { Name = "SceneUniqueVisual" };
    asset.AddChild(named); named.Owner = asset; named.UniqueNameInOwner = true;
    map.Bake();
    Assert.True(named.UniqueNameInOwner);
    Assert.True(named.Owner == asset);
    Assert.True(asset.GetNode("%SceneUniqueVisual") == named);
  }
}
