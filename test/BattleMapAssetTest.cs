using GdUnit4;
using Godot;
using System;
using static GdUnit4.Assertions;

[TestSuite]
[RequireGodotRuntime]
public partial class BattleMapAssetTest
{
  [TestCase]
  public void ExportAndReexportKeepModelAndResolvedFlagsSeparateFromTheSource()
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
    Assert.Equal(1, exported.GetChildCount());
    var floor = exported.GetNode<StaticBody3D>("FloorSurface");
    var shape = (CollisionShape3D)floor.GetChild(0);
    Assert.True((exported.Transform * floor.Transform * shape.Transform).Origin.IsEqualApprox(new(1.5f, -0.01f, 0.5f)));
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

  [TestCase]
  public void ExportCannotOverwriteItsEditableSourceScene()
  {
    var source = AutoFree(TestData.MakeEntityAnnotator(TestData.MakeFloorFootprint(1, 1)))!;
    const string path = "user://source_entity.tscn";
    source.ExportPath = "user://./source_entity.tscn";
    using var scene = new PackedScene();
    Assert.Equal(Error.Ok, scene.Pack(source));
    Assert.Equal(Error.Ok, ResourceSaver.Save(scene, path));
    var loaded = AutoFree(ResourceLoader.Load<PackedScene>(path, cacheMode: ResourceLoader.CacheMode.Ignore).Instantiate<BattleEntityAnnotator>())!;

    Assert.Throws<InvalidOperationException>(() => loaded.Export());

    var retained = AutoFree(ResourceLoader.Load<PackedScene>(path, cacheMode: ResourceLoader.CacheMode.IgnoreDeep).Instantiate<BattleEntityAnnotator>())!;
    Assert.True(retained.HasNode("Annotations"));
    Assert.True(retained.Annotations[Godot.Vector3I.Zero].HasFloor);
  }

  [TestCase("")]
  [TestCase("user://entity.tres")]
  [TestCase("entity.tscn")]
  public void ExportRequiresAnExplicitScenePath(string path)
  {
    var source = AutoFree(TestData.MakeEntityAnnotator(TestData.MakeFloorFootprint(1, 1)))!;
    source.ExportPath = path;
    Assert.Throws<InvalidOperationException>(() => source.Export());
  }

  [TestCase]
  public void ExportRequiresOneMeshModel()
  {
    var source = AutoFree(TestData.MakeEntityAnnotator())!;
    source.GetNode("Model").Free();
    source.AddChild(new Node3D { Name = "Model" });
    Assert.Throws<InvalidOperationException>(() => source.BuildScene());
  }
}
