#nullable enable
using FunProject.Battle;
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
    BattleTileState tile = board.GetTile(new Vector3I(1, 0, 1));

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
}
