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

  [TestCase(TestName = "BlocksVerticalLineOfSight bakes into runtime tile")]
  public void BlocksVerticalLineOfSightBakesIntoRuntimeTile()
  {
    BattleMapData mapWithFlag = TestData.MakeMapData(new Vector3I(4, 1, 4),
      (new Vector3I(1, 0, 1), new BattleMapTileData { BlocksVerticalLineOfSight = true }));
    BattleMapData mapWithoutFlag = TestData.MakeMapData(new Vector3I(4, 1, 4),
      (new Vector3I(1, 0, 1), new BattleMapTileData { BlocksVerticalLineOfSight = false }));

    BattleBoardState boardWith = new(mapWithFlag);
    BattleBoardState boardWithout = new(mapWithoutFlag);

    BattleTileState tileWith = boardWith.GetTile(boardWith.ValidatePoint(new Vector3I(1, 0, 1)).RequireSome());
    BattleTileState tileWithout = boardWithout.GetTile(boardWithout.ValidatePoint(new Vector3I(1, 0, 1)).RequireSome());

    Assert.True(tileWith.BlocksVerticalLineOfSight);
    Assert.False(tileWithout.BlocksVerticalLineOfSight);
  }
}
