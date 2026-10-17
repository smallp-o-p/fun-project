using GdUnit4;
using Godot;
using System;
using System.Threading.Tasks;
using static GdUnit4.Assertions;

namespace FunProject.Tests;

[TestSuite]
[RequireGodotRuntime]
public partial class BattleMapBakeTest
{
  [TestCase]
  public static async Task NativePaletteFloorPickingRespectsHolesAndIgnoresProps()
  {
    var source = TestData.MakeMapAuthoring();
    source.Palette = GD.Load<MeshLibrary>("res://resources/map_entities/MapPalette.tres");
    source.Floors.SetCellItem(Godot.Vector3I.Zero, 0);
    source.Floors.SetCellItem(new(2, 0, 0), 0);
    source.Props.SetCellItem(Godot.Vector3I.Zero, 1);
    var map = FunProject.Tests.GeoscapeTestScenes.AddToTree(source);
    var camera = FunProject.Tests.GeoscapeTestScenes.AddToTree(new Camera3D());
    var tree = (SceneTree)Engine.GetMainLoop();
    await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
    await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
    for (int x = 0; x < 3; x++)
    {
      var point = new Vector3(x + 0.5f, 0, 0.5f);
      camera.Position = point + Vector3.Up * 20;
      camera.LookAt(point, Vector3.Forward);
      var hit = GameCamera.RaycastViewportPosition(camera, map.GetWorld3D(), camera.GetViewport().GetVisibleRect().GetCenter());
      Assert.Equal(x != 1, hit.Count > 0);
      if (x != 1) Assert.True(hit["position"].AsVector3().IsEqualApprox(point));
    }
  }

  [TestCase]
  public static void NativePaintDataPaletteAndSpawnsSurviveSaveReload()
  {
    var source = AutoFree(TestData.MakeMapAuthoring())!;
    var palette = TestData.MakeTilePalette(new() { [42] = TestData.MakeFloorFootprint(2, 1) });
    source.Palette = palette;
    source.Floors.MeshLibrary = source.Props.MeshLibrary = palette;
    source.Floors.SetCellItem(Godot.Vector3I.Zero, 42);
    source.SpawnSlots[Godot.Vector3I.Right] = 1;
    source.Bake();
    using var packed = new PackedScene();
    Assert.Equal(Error.Ok, packed.Pack(source));
    const string path = "user://grid_map_roundtrip.tscn";
    Assert.Equal(Error.Ok, ResourceSaver.Save(packed, path));
    var map = AutoFree(ResourceLoader.Load<PackedScene>(path, cacheMode: ResourceLoader.CacheMode.IgnoreDeep).Instantiate<BattleMapAuthoring>())!;
    Assert.Equal(new Godot.Vector3I(2, 1, 1), map.MapData.Dimensions);
    using var savedSpawn = map.MapData.GetTile(Godot.Vector3I.Right);
    Assert.Equal(1, savedSpawn.SpawnFactionSlot);
    Assert.Equal(1, map.SpawnSlots[Godot.Vector3I.Right]);
    Assert.Equal(42, map.Floors.GetCellItem(Godot.Vector3I.Zero));
    Assert.True(BattleTilePalette.ReadFootprints(map.Palette)[42][Godot.Vector3I.Zero].HasFloor);
    Assert.True(map.Palette == map.Floors.MeshLibrary);
    map.ExportPath = "user://finished_map.tscn";
    map.Export();
    var exported = AutoFree(ResourceLoader.Load<PackedScene>(map.ExportPath, cacheMode: ResourceLoader.CacheMode.IgnoreDeep).Instantiate<BattleMap>())!;
    Assert.Equal(42, exported.FloorTiles.GetCellItem(Godot.Vector3I.Zero));
    using var exportedSpawn = exported.MapData.GetTile(Godot.Vector3I.Right);
    Assert.Equal(1, exportedSpawn.SpawnFactionSlot);
    Assert.Equal(2, exported.GetChildCount());
    Assert.True(exported.PropTiles.MeshLibrary == exported.FloorTiles.MeshLibrary);

  }

  [TestCase]
  public static void MapRootMustUseTheFixedBoardConvention()
  {
    var source = AutoFree(TestData.MakeMapAuthoring())!;
    source.Position = Vector3.Right;
    source.Floors.SetCellItem(Godot.Vector3I.Zero, 0);
    Assert.Throws<InvalidOperationException>(() => source.BuildData(new() { [0] = TestData.MakeFloorFootprint(1, 1) }));
  }

  [TestCase(0)]
  [TestCase(1)]
  [TestCase(2)]
  public static void SpawnRequiresNonnegativeSlotAndWalkableGroundCell(int scenario)
  {
    var source = AutoFree(TestData.MakeMapAuthoring())!;
    var footprint = TestData.MakeFloorFootprint(1, 1);
    if (scenario == 2) footprint[Godot.Vector3I.Zero].BlocksMovement = true;
    source.Floors.SetCellItem(Godot.Vector3I.Zero, 0);
    source.SpawnSlots[scenario == 0 ? Godot.Vector3I.Right : Godot.Vector3I.Zero] = scenario == 1 ? -1 : 0;
    Assert.Throws<InvalidOperationException>(() => source.BuildData(new() { [0] = footprint }));
  }

  [TestCase]
  public static void InvalidBakePreservesStoredData()
  {
    var source = AutoFree(TestData.MakeMapAuthoring())!;
    source.Palette = TestData.MakeTilePalette(new() { [0] = TestData.MakeFloorFootprint(1, 1) });
    source.Floors.SetCellItem(Godot.Vector3I.Zero, 0);
    source.SpawnSlots[Godot.Vector3I.Right] = 0;
    var previous = source.MapData;
    Assert.Throws<InvalidOperationException>(() => source.Bake());
    Assert.True(source.MapData == previous);
  }
}
