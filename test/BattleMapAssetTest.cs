using GdUnit4;
using Godot;
using static GdUnit4.Assertions;

[TestSuite]
[RequireGodotRuntime]
public partial class BattleMapAssetTest
{
  [TestCase]
  public void ReusableAssetsRoundTripAnnotationsAndVisuals()
  {
    var root = AutoFree(new Node3D { Name = "Asset" })!;
    TestData.Annotate(root, new()
    {
      [Godot.Vector3I.Zero] = new() { BlocksMovement = true, WalkableTop = true },
      [new(1, 0, 0)] = new() { HasFloor = true }
    });
    var visual = new MeshInstance3D { Name = "Visual", Mesh = new BoxMesh(), Scale = new(2, 3, 1) };
    root.AddChild(visual); visual.Owner = root;
    using var packed = new PackedScene();
    Assert.Equal(Error.Ok, packed.Pack(root));
    const string path = "user://assets_roundtrip.tscn";
    Assert.Equal(Error.Ok, ResourceSaver.Save(packed, path));
    var reloaded = AutoFree(ResourceLoader.Load<PackedScene>(path, cacheMode: ResourceLoader.CacheMode.Ignore).Instantiate<Node3D>())!;
    var loaded = reloaded.GetNode<BattleAnnotationGrid>("Annotations");
    Assert.True(loaded.LayoutMatches());
    Assert.True(loaded.Validate().IsNone);
    Assert.Equal(Vector3.One, loaded.CellSize);
    Assert.True(loaded.Annotations[Godot.Vector3I.Zero].BlocksMovement);
    Assert.True(loaded.Annotations[new(1, 0, 0)].HasFloor);
    Assert.Equal(new Vector3(2, 3, 1), reloaded.GetNode<Node3D>("Visual").Scale);
  }
}
