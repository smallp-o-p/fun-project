using FunProject.Battle;
using GdUnit4;
using Godot;

[TestSuite]
[RequireGodotRuntime]
public partial class BattleMapDataTest
{
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
        CoverNorth = 10,
        CoverEast = 30,
        CoverSouth = 50,
        CoverWest = 70
      }));

    BattleBoardState board = new(mapData);
    BattleTileState coveredTile = board.GetTile(board.ValidatePoint(new Vector3I(1, 0, 1)).RequireSome());
    BattleTileState absentTile = board.GetTile(board.ValidatePoint(new Vector3I(2, 0, 2)).RequireSome());

    Assert.Equal(new TileCover(10, 30, 50, 70), coveredTile.Cover);
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

  [TestCase(TestName = "Directional cover strengths are independent")]
  public void DirectionalCoverStrengthsAreIndependent()
  {
    var tile = new BattleMapTileData { CoverNorth = 10, CoverEast = 30, CoverSouth = 50, CoverWest = 70 };

    tile.CoverNorth = 0;

    Assert.Equal(0, tile.CoverNorth);
    Assert.Equal(30, tile.CoverEast);
    Assert.Equal(50, tile.CoverSouth);
    Assert.Equal(70, tile.CoverWest);
  }

  [TestCase(-1, 0)]
  [TestCase(0, 0)]
  [TestCase(37, 37)]
  [TestCase(100, 100)]
  [TestCase(101, 100)]
  public void DirectionalCoverStrengthsClampToPercentageRange(int value, int expected)
  {
    var tile = new BattleMapTileData { CoverNorth = value, CoverEast = value, CoverSouth = value, CoverWest = value };

    Assert.Equal(expected, tile.CoverNorth);
    Assert.Equal(expected, tile.CoverEast);
    Assert.Equal(expected, tile.CoverSouth);
    Assert.Equal(expected, tile.CoverWest);
  }

  [TestCase]
  public void GroundSurfaceOffsetDefaultsToZero() =>
    Assert.Equal(0f, new BattleMapTileData().GroundSurfaceOffset);

  [TestCase(TestName = "Walkable, BlocksLineOfSight and cover are independent")]
  public void WalkableBlocksLineOfSightAndCoverAreIndependent()
  {
    var smoke = new BattleMapTileData { Walkable = true, BlocksLineOfSight = true };
    Assert.True(smoke.Walkable);
    Assert.True(smoke.BlocksLineOfSight);
    Assert.Equal(0, smoke.CoverNorth);
    Assert.Equal(0, smoke.CoverEast);
    Assert.Equal(0, smoke.CoverSouth);
    Assert.Equal(0, smoke.CoverWest);

    var wall = new BattleMapTileData { Walkable = false, BlocksLineOfSight = true };
    Assert.False(wall.Walkable);
    Assert.Equal(0, wall.CoverNorth);
    Assert.Equal(0, wall.CoverEast);
    Assert.Equal(0, wall.CoverSouth);
    Assert.Equal(0, wall.CoverWest);
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
