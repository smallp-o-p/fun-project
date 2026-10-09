using GdUnit4;
using Godot;
using static GdUnit4.Assertions;

namespace FunProject.Tests;

[TestSuite]
[RequireGodotRuntime]
public partial class BattleMapAssetTest
{
  [TestCase]
  public static void ExportAndReexportKeepModelAndResolvedFlagsSeparateFromTheSource()
  {
    var flags = new BattleFootprintData { CoverAmount = 25 };
    var source = AutoFree(TestData.MakeEntityAnnotator(new() { [Godot.Vector3I.Zero] = flags }))!;
    source.ExportPath = "user://exported_entity.tscn";
    source.Grid.SetCellItem(Godot.Vector3I.Zero, 2);
    source.Grid.SetCellItem(Godot.Vector3I.Right, 1);
    var visual = source.GetNode<MeshInstance3D>("Model");
    visual.Position = new(1, 2, 3);
    visual.Scale = new(2, 3, 4);
    visual.MaterialOverride = new StandardMaterial3D { AlbedoColor = Colors.Red };

    source.Export();
    var exported = AutoFree(ResourceLoader.Load<PackedScene>(source.ExportPath).Instantiate<BattleProp>())!;
    Assert.Equal(2, exported.Footprint.Count);
    Assert.False(exported.Footprint[Godot.Vector3I.Zero].BlocksMovement);
    Assert.True(exported.Footprint[Godot.Vector3I.Right].HasFloor);
    Assert.Equal(0, exported.GetChildCount());
    Assert.Equal(visual.Transform, exported.Transform);
    Assert.Equal(Colors.Red, ((StandardMaterial3D)exported.Mesh.SurfaceGetMaterial(0)).AlbedoColor);
    Assert.True(exported.Mesh.HasMeta(BattleProp.FootprintMeta));
    Assert.True(source.HasNode("Annotations"));

    flags.CoverAmount = 60;
    source.Grid.SetCellItem(Godot.Vector3I.Right, -1);
    ((BoxMesh)visual.Mesh).Size = new(4, 5, 6);
    source.Export();
    var updated = AutoFree(ResourceLoader.Load<PackedScene>(source.ExportPath).Instantiate<BattleProp>())!;
    Assert.Equal(1, updated.Footprint.Count);
    Assert.Equal(60, updated.Footprint[Godot.Vector3I.Zero].CoverAmount);
    Assert.Equal(new Vector3(4, 5, 6), ((BoxMesh)updated.Mesh).Size);
    Assert.Equal(25, exported.Footprint[Godot.Vector3I.Zero].CoverAmount);
  }

}
