using GdUnit4;
using Godot;
using static GdUnit4.Assertions;

namespace FunProject.Tests;

[TestSuite]
[RequireGodotRuntime]
public partial class BattleMapPaletteTest
{
  [TestCase]
  public static void BrushMeshMetadataSurvivesSaveReloadAndFieldEdits()
  {
    var marker = AutoFree(new BattleAnnotationMarker { Mesh = new BoxMesh() })!;
    marker.Defaults = new BattleFootprintData { HasFloor = true };
    marker.Defaults.CoverAmount = 45;
    const string path = "user://brush_mesh.tres";
    Assert.Equal(Error.Ok, ResourceSaver.Save(marker.Mesh, path));
    var mesh = ResourceLoader.Load<Mesh>(path, cacheMode: ResourceLoader.CacheMode.IgnoreDeep);
    var defaults = mesh.GetMeta(BattleAnnotationMarker.FootprintMeta).As<BattleFootprintData>();
    Assert.True(defaults.HasFloor);
    Assert.Equal(45, defaults.CoverAmount);
  }

  [TestCase]
  public static void NativePaletteKeepsNonsequentialIdsAndMeshMetadata()
  {
    var palette = TestData.MakeTilePalette(new() { [42] = TestData.MakeFloorFootprint(2, 1), [7] = [] });
    palette.SetItemMeshTransform(42, new(Basis.Identity, new(1, 2, 3)));
    const string path = "user://tile_palette.tres";
    Assert.Equal(Error.Ok, ResourceSaver.Save(palette, path));
    var loaded = ResourceLoader.Load<MeshLibrary>(path, cacheMode: ResourceLoader.CacheMode.IgnoreDeep);
    var footprints = BattleTilePalette.ReadFootprints(loaded);
    Assert.Equal(2, footprints[42].Count);
    Assert.True(footprints[42][Godot.Vector3I.Right].HasFloor);
    Assert.Equal(0, footprints[7].Count);
    Assert.Equal(new Vector3(1, 2, 3), loaded.GetItemMeshTransform(42).Origin);
  }
}
