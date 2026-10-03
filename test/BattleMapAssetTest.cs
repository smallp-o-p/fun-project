using GdUnit4;
using Godot;
using static GdUnit4.Assertions;

[TestSuite]
[RequireGodotRuntime]
public partial class BattleMapAssetTest
{
  [TestCase]
  public void ReusableTypedAssetsRoundTripSharedDataAndIndependentOverrides()
  {
    var root = AutoFree(new Node3D())!;
    var footprint = new BattleFootprintData
    {
      Cells = { [Godot.Vector3I.Zero] = new BattleFootprintCellData { BlocksMovement = true, WalkableTop = true },
        [new(1, 0, 0)] = new BattleFootprintCellData { HasFloor = true, SpawnFactionSlot = 2 } }
    };
    var first = new BattleProp { Name = "First", Footprint = footprint };
    var second = new BattleProp { Name = "Second", Footprint = (BattleFootprintData)footprint.Duplicate(true), Position = Vector3.Right * 4 };
    second.Footprint.Cells[Godot.Vector3I.Zero].BlocksMovement = false;
    var floor = new BattleFloor { Name = "Floor", Footprint = footprint };
    root.AddChild(first); first.Owner = root;
    root.AddChild(second); second.Owner = root;
    root.AddChild(floor); floor.Owner = root;
    var visual = new MeshInstance3D { Name = "Visual", Mesh = new BoxMesh(), Scale = new(2, 3, 1) };
    first.AddChild(visual); visual.Owner = root;
    using var packed = new PackedScene();
    Assert.Equal(Error.Ok, packed.Pack(root));
    const string path = "user://typed_assets_roundtrip.tscn";
    Assert.Equal(Error.Ok, ResourceSaver.Save(packed, path));
    var reloaded = AutoFree(ResourceLoader.Load<PackedScene>(path, cacheMode: ResourceLoader.CacheMode.Ignore).Instantiate<Node3D>())!;
    Assert.True(reloaded.GetNode<BattleProp>("First").Footprint.Cells[Godot.Vector3I.Zero].BlocksMovement);
    Assert.False(reloaded.GetNode<BattleProp>("Second").Footprint.Cells[Godot.Vector3I.Zero].BlocksMovement);
    Assert.Equal(2, reloaded.GetNode<BattleFloor>("Floor").Footprint.Cells[new(1, 0, 0)].SpawnFactionSlot);
    Assert.Equal(new Vector3(2, 3, 1), reloaded.GetNode<Node3D>("First/Visual").Scale);
    Assert.Equal(0, AutoFree(new BattleProp())!.Footprint.Cells.Count);
  }
}
