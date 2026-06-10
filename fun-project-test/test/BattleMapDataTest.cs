using FunProject.Battle;
using FunProject.Tests;
using GdUnit4;
using Godot;
using System.Linq;

[TestSuite]
[RequireGodotRuntime]
public partial class BattleMapDataTest
{
  [TestCase(TestName = "CreateBoardState applies tile overrides to runtime tiles")]
  public void CreateBoardStateAppliesTileOverridesToRuntimeTiles()
  {
    var mapData = new BattleMapData
    {
      Dimensions = new Vector3I(4, 1, 4),
      TileOverrides =
      [
        new BattleMapTileData
        {
          Coordinates = new Vector3I(1, 0, 1),
          IsWalkable = false,
          BlocksLineOfSight = true
        }
      ]
    };

    BattleBoardState board = mapData.CreateBoardState();
    BattleTileState tile = board.GetTile(board.ValidatePoint(new Vector3I(1, 0, 1)).RequireSome());

    Assert.False(tile.IsWalkable);
    Assert.True(tile.BlocksLineOfSight);
  }

  [TestCase(TestName = "EnumeratePresentCells skips absent override cells")]
  public void EnumeratePresentCellsSkipsAbsentOverrideCells()
  {
    var mapData = new BattleMapData
    {
      Dimensions = new Vector3I(2, 1, 2),
      TileOverrides =
      [
        new BattleMapTileData
        {
          Coordinates = new Vector3I(1, 0, 0),
          IsPresent = false
        }
      ]
    };

    Vector3I[] cells = [.. mapData.EnumeratePresentCells()];

    Assert.Equal(3, cells.Length);
    Assert.False(cells.Contains(new Vector3I(1, 0, 0)));
  }

  [TestCase(TestName = "CreateBoardState copies cover into runtime tiles")]
  public void CreateBoardStateCopiesCoverIntoRuntimeTiles()
  {
    var mapData = new BattleMapData
    {
      Dimensions = new Vector3I(4, 1, 4),
      TileOverrides =
      [
        new BattleMapTileData
        {
          Coordinates = new Vector3I(1, 0, 1),
          CoverDirections = CoverDirections.North | CoverDirections.East,
          CoverAmount = 40
        }
      ]
    };

    BattleBoardState board = mapData.CreateBoardState();
    BattleTileState coveredTile = board.GetTile(board.ValidatePoint(new Vector3I(1, 0, 1)).RequireSome());
    BattleTileState defaultTile = board.GetTile(board.ValidatePoint(new Vector3I(2, 0, 2)).RequireSome());

    Assert.Equal(new TileCover(CoverDirections.North | CoverDirections.East, 40), coveredTile.Cover);
    Assert.Equal(TileCover.None, defaultTile.Cover);
  }

  [TestCase(TestName = "CreateBoardState clears cover on absent tiles")]
  public void CreateBoardStateClearsCoverOnAbsentTiles()
  {
    var mapData = new BattleMapData
    {
      Dimensions = new Vector3I(4, 1, 4),
      TileOverrides =
      [
        new BattleMapTileData
        {
          Coordinates = new Vector3I(1, 0, 1),
          IsPresent = false,
          CoverDirections = CoverDirections.North,
          CoverAmount = 40
        }
      ]
    };

    BattleBoardState board = mapData.CreateBoardState();
    BattleTileState tile = board.GetTile(board.ValidatePoint(new Vector3I(1, 0, 1)).RequireSome());

    Assert.Equal(TileCover.None, tile.Cover);
  }

  [TestCase(TestName = "Clearing cover directions zeroes cover amount")]
  public void ClearingCoverDirectionsZeroesCoverAmount()
  {
    var tileData = new BattleMapTileData { CoverDirections = CoverDirections.North, CoverAmount = 40 };
    Assert.Equal(40, tileData.CoverAmount);

    tileData.CoverDirections = CoverDirections.None;
    Assert.Equal(0, tileData.CoverAmount);

    tileData.CoverAmount = 50;
    Assert.Equal(0, tileData.CoverAmount);
  }
}
