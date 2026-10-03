using GdUnit4;
using Godot;
using static GdUnit4.Assertions;

[TestSuite]
[RequireGodotRuntime]
public partial class BattleMapAssetTest
{
  [TestCase]
  public void ReusableTypedAssetsRoundTripAnnotationsAndVisuals()
  {
    var root = AutoFree(new BattleProp { Name = "Asset" })!;
    TestData.Annotate(root, new BattleFootprintData
    {
      Cells = { [Godot.Vector3I.Zero] = new BattleFootprintCellData { BlocksMovement = true, WalkableTop = true },
        [new(1, 0, 0)] = new BattleFootprintCellData { HasFloor = true, SpawnFactionSlot = 2 } }
    });
    var visual = new MeshInstance3D { Name = "Visual", Mesh = new BoxMesh(), Scale = new(2, 3, 1) };
    root.AddChild(visual); visual.Owner = root;
    using var packed = new PackedScene();
    Assert.Equal(Error.Ok, packed.Pack(root));
    const string path = "user://typed_assets_roundtrip.tscn";
    Assert.Equal(Error.Ok, ResourceSaver.Save(packed, path));
    var reloaded = AutoFree(ResourceLoader.Load<PackedScene>(path, cacheMode: ResourceLoader.CacheMode.Ignore).Instantiate<BattleProp>())!;
    var loaded = reloaded.GetNode<BattleAnnotationGrid>("Annotations");
    Assert.True(loaded.LayoutMatches());
    Assert.True(loaded.Annotations.Cells[Godot.Vector3I.Zero].BlocksMovement);
    Assert.Equal(2, loaded.Annotations.Cells[new(1, 0, 0)].SpawnFactionSlot);
    Assert.Equal(new Vector3(2, 3, 1), reloaded.GetNode<Node3D>("Visual").Scale);
  }
}
