using FunProject.Battle;
using GdUnit4;
using Godot;
using System.Security.Cryptography;
using System.Text;
using static GdUnit4.Assertions;
using Cell = Godot.Vector3I;

[TestSuite]
[RequireGodotRuntime]
public partial class BattleMapMigrationTest
{
  [TestCase("DebugMap", "debug_map", 8, 2, 7, 56, "7D2EF1CC1E71A5B530E6001F8E40BC91756753D16B2748F52B2164B827BED518")]
  [TestCase("DefusalMap", "defusal_map", 8, 1, 8, 64, "0F4860C9224025E8F3FC01F3D2BCF7547AEA59F6E36A1033A99E117D11EAD8A4")]
  [TestCase("BattleMapAuthoring", "prop_example_map", 5, 1, 5, 25, "50DE9E17A8DB8FA01BFCEF0F169D2A4D6358B1BE75845E1DEDD7903F000FA1C0")]
  public void NativeAnnotationsPreserveEveryPreMigrationGameplayCell(
    string sourceName, string exportedName, int width, int height, int depth, int count, string expectedFingerprint)
  {
    var source = AutoFree(GD.Load<PackedScene>($"res://scenes/battle/authoring/{sourceName}.tscn")
      .Instantiate<BattleMapAuthoring>())!;
    using var packed = source.BuildScene();
    var rebuilt = AutoFree(packed.Instantiate<BattleMap>())!;
    var shipped = AutoFree(GD.Load<PackedScene>($"res://resources/maps/{exportedName}.tscn")
      .Instantiate<BattleMap>())!;

    Assert.Equal(new Cell(width, height, depth), rebuilt.MapData.Dimensions);
    Assert.Equal(count, rebuilt.MapData.Tiles.Count);
    Assert.Equal(Vector3.Zero, rebuilt.MapData.GridOrigin);
    Assert.Equal(1f, rebuilt.MapData.CellWidth);
    Assert.Equal(1f, rebuilt.MapData.LevelHeight);
    Assert.Equal(expectedFingerprint, GameplayFingerprint(rebuilt.MapData));
    Assert.Equal(expectedFingerprint, GameplayFingerprint(shipped.MapData));
    AssertNoAnnotationMarkers(rebuilt);
    AssertNoAnnotationMarkers(shipped);

    string roundTripPath = $"user://native_{exportedName}_roundtrip.tscn";
    Assert.Equal(Error.Ok, ResourceSaver.Save(packed, roundTripPath));
    var reloaded = AutoFree(ResourceLoader.Load<PackedScene>(roundTripPath,
      cacheMode: ResourceLoader.CacheMode.Ignore).Instantiate<BattleMap>())!;
    Assert.Equal(expectedFingerprint, GameplayFingerprint(reloaded.MapData));
    AssertNoAnnotationMarkers(reloaded);
  }

  [TestCase("FloorTile", 1, -1)]
  [TestCase("SpawnFloorSlot0", 1, 0)]
  [TestCase("SpawnFloorSlot1", 1, 1)]
  [TestCase("Block", 1, -1)]
  [TestCase("Car", 2, -1)]
  [TestCase("proof/AnnotationProofFloor", 12, -1)]
  public void ReusableAssetsStoreGameplayInTheirDirectNativeAnnotationGrid(string assetName, int cellCount, int spawnSlot)
  {
    var asset = AutoFree(GD.Load<PackedScene>($"res://scenes/battle/authoring/{assetName}.tscn")
      .Instantiate<Node3D>())!;
    var grid = asset.GetNode<BattleAnnotationGrid>("Annotations");
    Assert.True(ReferenceEquals(asset, grid.GetParent()));
    Assert.True(ReferenceEquals(asset, grid.Owner));
    Assert.Equal(cellCount, grid.GetUsedCells().Count);
    Assert.True(grid.LayoutMatches());
    Assert.Equal("", grid.AuthoringProblem(Vector3.One));
    var footprint = grid.BuildFootprint(Vector3.One);
    Assert.Equal(cellCount, footprint.Cells.Count);
    Assert.Equal(spawnSlot, footprint.Cells[Cell.Zero].SpawnFactionSlot);
  }

  [TestCase("DebugMap")]
  [TestCase("DefusalMap")]
  public void SpawnFloorsAreReusableAssetsWithoutMapInstanceAnnotationOverrides(string sourceName)
  {
    var source = AutoFree(GD.Load<PackedScene>($"res://scenes/battle/authoring/{sourceName}.tscn")
      .Instantiate<BattleMapAuthoring>())!;
    foreach (var child in source.GetChildren())
    {
      if (child is not BattleFloor floor) continue;
      var grid = floor.GetNode<BattleAnnotationGrid>("Annotations");
      int slot = grid.Annotations.Cells[Cell.Zero].SpawnFactionSlot;
      string name = slot < 0 ? "FloorTile" : $"SpawnFloorSlot{slot}";
      Assert.Equal($"res://scenes/battle/authoring/{name}.tscn", floor.SceneFilePath);
      Assert.True(ReferenceEquals(floor, grid.Owner));
      var template = AutoFree(GD.Load<PackedScene>(floor.SceneFilePath).Instantiate<BattleFloor>())!;
      Assert.True(ReferenceEquals(template.GetNode<BattleAnnotationGrid>("Annotations").Annotations, grid.Annotations));
    }
  }

  [TestCase("DebugMap")]
  [TestCase("DefusalMap")]
  public void ScenarioMapsKeepMasterCoordinatesMovementSightAndSpawns(string sourceName)
  {
    bool debug = sourceName == "DebugMap";
    int depth = debug ? 7 : 8;
    var source = AutoFree(GD.Load<PackedScene>($"res://scenes/battle/authoring/{sourceName}.tscn")
      .Instantiate<BattleMapAuthoring>())!;
    using var packed = source.BuildScene();
    var map = AutoFree(packed.Instantiate<BattleMap>())!;
    Assert.Equal(8 * depth, map.MapData.Tiles.Count);
    for (int x = 0; x < 8; x++)
      for (int z = 0; z < depth; z++)
      {
        bool solid = debug && x is 1 or 2 or 5 or 6 && z is >= 2 and <= 4;
        var cell = new Cell(x, solid ? 1 : 0, z);
        var tile = map.MapData.Tiles[cell];
        int spawnSlot = debug
          ? ((x <= 1 && z >= 5) || (x >= 6 && z <= 1) ? 0 : -1)
          : (x <= 2 && z == 0 ? 0 : x >= 5 && z == 7 ? 1 : -1);
        Assert.Equal(!solid, tile.Walkable);
        Assert.Equal(solid, tile.BlocksLineOfSight);
        Assert.False(tile.BlocksVerticalLineOfSight);
        Assert.Equal(spawnSlot, tile.SpawnFactionSlot);
        // Master stored all-sided 60 cover on these non-walkable Y=1 solids.
        // Native prop faces grant cover only to adjacent standable cells; there
        // are no Y=1 floor cells here, so no usable cover is removed or added.
        Assert.Equal(CoverDirections.None, tile.CoverDirections);
        Assert.Equal(0, tile.CoverAmount);
      }
  }

  [TestCase]
  public void DefusalMapKeepsBothAuthoredBombCellsWalkable()
  {
    var source = AutoFree(GD.Load<PackedScene>("res://scenes/battle/authoring/DefusalMap.tscn")
      .Instantiate<BattleMapAuthoring>())!;
    using var packed = source.BuildScene();
    var map = AutoFree(packed.Instantiate<BattleMap>())!;
    Assert.True(map.MapData.Tiles[new(3, 0, 3)].Walkable);
    Assert.True(map.MapData.Tiles[new(4, 0, 4)].Walkable);
  }

  private static void AssertNoAnnotationMarkers(Node root)
  {
    Assert.False(root is BattleAnnotationGrid);
    foreach (var child in root.GetChildren()) AssertNoAnnotationMarkers(child);
  }

  // Pinned before migration from the shipped scenes. Each sorted row includes the
  // coordinate, walkability, both LOS flags, cover sides/amount and spawn slot.
  private static string GameplayFingerprint(BattleMapData data)
  {
    var rows = new StringBuilder();
    foreach (var cell in data.Tiles.Keys.AsValueEnumerable().OrderBy(cell => cell.X)
      .ThenBy(cell => cell.Y).ThenBy(cell => cell.Z))
    {
      if (rows.Length > 0) rows.Append(';');
      var tile = data.Tiles[cell];
      rows.Append($"{cell.X},{cell.Y},{cell.Z}:{(tile.Walkable ? 1 : 0)}," +
        $"{(tile.BlocksLineOfSight ? 1 : 0)},{(tile.BlocksVerticalLineOfSight ? 1 : 0)}," +
        $"{(int)tile.CoverDirections},{tile.CoverAmount},{tile.SpawnFactionSlot}");
    }
    return System.Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rows.ToString())));
  }
}
