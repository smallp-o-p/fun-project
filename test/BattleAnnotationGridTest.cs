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

  private static void AssertOutlineMarker(Mesh mesh)
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
    Assert.True(marker.SurfaceGetMaterial(0) is StandardMaterial3D);
    var material = (StandardMaterial3D)marker.SurfaceGetMaterial(0);
    Assert.Equal(new Color(0.15f, 0.85f, 0.75f, 1), material.AlbedoColor);
    Assert.Equal(BaseMaterial3D.TransparencyEnum.Disabled, material.Transparency);
    Assert.Equal(BaseMaterial3D.ShadingModeEnum.Unshaded, material.ShadingMode);
    Assert.True(material.NoDepthTest);

    static int Corner(Vector3 vertex)
    {
      Assert.True(vertex.Abs().IsEqualApprox(Vector3.One * 0.48f), "Edges must meet at the inset box corners.");
      return (vertex.X > 0 ? 1 : 0) | (vertex.Y > 0 ? 2 : 0) | (vertex.Z > 0 ? 4 : 0);
    }
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

  [TestCase]
  public void ExactlyOneOccupiedCellIsRequired()
  {
    var grid = AutoFree(new BattleAnnotationGrid())!;
    grid.SetCellItem(Godot.Vector3I.Zero, 0);
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
