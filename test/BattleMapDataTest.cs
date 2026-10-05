using FunProject.Battle;
using GdUnit4;
using Godot;

[TestSuite]
[RequireGodotRuntime]
public partial class BattleMapDataTest
{
  [TestCase]
  public void BoardConstructionKeepsLargeEmptyVolumesCompact()
  {
    // Tall holes still participate in 3D LOS, but must not allocate one object per cell.
    _ = new BattleBoardState(new Vector3I(1, 1, 1));
    long before = System.GC.GetAllocatedBytesForCurrentThread();
    var board = new BattleBoardState(new Vector3I(17, 19, 23));
    long allocated = System.GC.GetAllocatedBytesForCurrentThread() - before;
    Assert.True(allocated < 17 * 19 * 23 * 4 + 4096);
    System.GC.KeepAlive(board);
  }

  [TestCase]
  public void RetainedTileAccessMutatesOnlyItsCellAndBoard()
  {
    using var map = TestData.MakeMapData(new Vector3I(3, 2, 5),
      (new Vector3I(2, 1, 4), TestData.FloorTile()));
    var first = new BattleBoardState(map);
    var second = new BattleBoardState(map);
    var point = first.At(2, 1, 4);
    var retained = first.GetTile(point);
    retained.BlocksLineOfSight = true;
    retained.BlocksVerticalLineOfSight = true;
    retained.Cover = new TileCover(CoverDirections.East, 40);
    first.SetTileWalkable(point, false);
    Assert.False(retained.IsWalkable);
    Assert.True(first.GetTile(point).BlocksLineOfSight);
    Assert.True(first.GetTile(point).BlocksVerticalLineOfSight);
    Assert.Equal(new TileCover(CoverDirections.East, 40), first.GetTile(point).Cover);
    Assert.True(second.GetTile(point).IsWalkable);
    Assert.False(second.GetTile(point).BlocksLineOfSight);
    Assert.False(second.GetTile(point).BlocksVerticalLineOfSight);
    Assert.Equal(TileCover.None, second.GetTile(point).Cover);
    foreach (var other in first.EnumerateBoardPoints())
    {
      if (other == point) continue;
      Assert.False(first.GetTile(other).IsWalkable);
      Assert.False(first.GetTile(other).BlocksLineOfSight);
      Assert.False(first.GetTile(other).BlocksVerticalLineOfSight);
      Assert.Equal(TileCover.None, first.GetTile(other).Cover);
    }
  }

  [TestCase]
  public void LegacyMapLoadsAndResavesPackedCellsWithoutLosingSpawnsOrCeilings()
  {
    using var map = new BattleMapData();
    // Exercise the old serialized property before Dimensions to guard load ordering.
    map.Set("Tiles", new Godot.Collections.Dictionary<Godot.Vector3I, BattleMapTileData>
    {
      [new(2, 1, 4)] = new()
      {
        Walkable = true,
        BlocksVerticalLineOfSight = true,
        CoverDirections = CoverDirections.West,
        CoverAmount = 60,
        SpawnFactionSlot = 3
      }
    });
    map.Dimensions = new(3, 2, 5);
    const string path = "user://compact_map_roundtrip.tres";
    Assert.Equal(Error.Ok, ResourceSaver.Save(map, path));
    using var reopened = ResourceLoader.Load<BattleMapData>(path, cacheMode: ResourceLoader.CacheMode.IgnoreDeep);
    Assert.Equal(new BattleMapCell(true, false, true, CoverDirections.West, 60, 3), reopened.GetTile(new(2, 1, 4)));
    Assert.Equal(new BattleMapCell(false, false, false, CoverDirections.None, 0, -1), reopened.GetTile(new(1, 1, 4)));
    using var file = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Read);
    Assert.False(file.GetAsText().Contains("BattleMapTileData"));
  }

  [TestCase(TestName = "Board from map data applies tile data to runtime tiles")]
  public void BoardFromMapDataAppliesTileDataToRuntimeTiles()
  {
    BattleMapData mapData = TestData.MakeMapData(new Vector3I(4, 1, 4),
      (new Vector3I(1, 0, 1), TestData.WallTile()));

    BattleBoardState board = new(mapData);
    BattleTileState tile = board.GetTile(board.ValidatePoint(new Vector3I(1, 0, 1)).RequireSome());

    Assert.False(tile.IsWalkable);
    Assert.True(tile.BlocksLineOfSight);
  }

  [TestCase(TestName = "Board from map data copies cover into runtime tiles")]
  public void BoardFromMapDataCopiesCoverIntoRuntimeTiles()
  {
    BattleMapData mapData = TestData.MakeMapData(new Vector3I(4, 1, 4),
      (new Vector3I(1, 0, 1), new BattleMapTileData
      {
        CoverDirections = CoverDirections.North | CoverDirections.East,
        CoverAmount = 40
      }));

    BattleBoardState board = new(mapData);
    BattleTileState coveredTile = board.GetTile(board.ValidatePoint(new Vector3I(1, 0, 1)).RequireSome());
    BattleTileState absentTile = board.GetTile(board.ValidatePoint(new Vector3I(2, 0, 2)).RequireSome());

    Assert.Equal(new TileCover(CoverDirections.North | CoverDirections.East, 40), coveredTile.Cover);
    Assert.Equal(TileCover.None, absentTile.Cover);
  }

  [TestCase(TestName = "Cells not in the dictionary are holes")]
  public void CellsNotInTheDictionaryAreHoles()
  {
    BattleMapData mapData = TestData.MakeMapData(new Vector3I(4, 1, 4),
      (new Vector3I(1, 0, 1), TestData.FloorTile()));

    BattleBoardState board = new(mapData);
    BattleTileState listed = board.GetTile(board.ValidatePoint(new Vector3I(1, 0, 1)).RequireSome());
    BattleTileState unlisted = board.GetTile(board.ValidatePoint(new Vector3I(0, 0, 0)).RequireSome());

    Assert.True(listed.IsWalkable);
    Assert.False(unlisted.IsWalkable);
  }

  [TestCase(TestName = "Board from map data supports stacked levels")]
  public void BoardFromMapDataSupportsStackedLevels()
  {
    var mapData = TestData.MakeMapData(new Vector3I(2, 2, 2),
      (new Vector3I(0, 0, 0), TestData.FloorTile()),
      (new Vector3I(0, 1, 0), TestData.FloorTile()));

    BattleBoardState board = new(mapData);
    BattleTileState ground = board.GetTile(board.ValidatePoint(new Vector3I(0, 0, 0)).RequireSome());
    BattleTileState upper = board.GetTile(board.ValidatePoint(new Vector3I(0, 1, 0)).RequireSome());
    BattleTileState empty = board.GetTile(board.ValidatePoint(new Vector3I(1, 1, 1)).RequireSome());

    Assert.True(ground.IsWalkable);
    Assert.True(upper.IsWalkable);
    Assert.False(empty.IsWalkable);
  }

  [TestCase(TestName = "Clearing cover directions zeroes cover amount")]
  public void ClearingCoverDirectionsZeroesCoverAmount()
  {
    var tile = new BattleMapTileData { CoverDirections = CoverDirections.North, CoverAmount = 40 };
    Assert.Equal(40, tile.CoverAmount);

    tile.CoverDirections = CoverDirections.None;
    Assert.Equal(0, tile.CoverAmount);

    tile.CoverAmount = 50;
    Assert.Equal(0, tile.CoverAmount);
  }

  [TestCase(TestName = "Walkable, BlocksLineOfSight and cover are independent")]
  public void WalkableBlocksLineOfSightAndCoverAreIndependent()
  {
    var smoke = new BattleMapTileData { Walkable = true, BlocksLineOfSight = true };
    Assert.True(smoke.Walkable);
    Assert.True(smoke.BlocksLineOfSight);
    Assert.Equal(CoverDirections.None, smoke.CoverDirections);

    var wall = new BattleMapTileData { Walkable = false, BlocksLineOfSight = true };
    Assert.False(wall.Walkable);
    Assert.Equal(0, wall.CoverAmount);
  }

  [TestCase(true, TestName = "BlocksVerticalLineOfSight bakes into runtime tile")]
  [TestCase(false, TestName = "A tile without the vertical flag bakes false")]
  public void BlocksVerticalLineOfSightBakesIntoRuntimeTile(bool flag)
  {
    BattleMapData mapData = TestData.MakeMapData(new Vector3I(4, 1, 4),
      (new Vector3I(1, 0, 1), new BattleMapTileData { BlocksVerticalLineOfSight = flag }));

    BattleBoardState board = new(mapData);
    BattleTileState tile = board.GetTile(board.ValidatePoint(new Vector3I(1, 0, 1)).RequireSome());

    Assert.Equal(flag, tile.BlocksVerticalLineOfSight);
  }
}
