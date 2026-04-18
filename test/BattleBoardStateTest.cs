#nullable enable
using FunProject.Battle;
using GdUnit4;
using Godot;
using System.Linq;

[TestSuite]
[RequireGodotRuntime]
public partial class BattleBoardStateTest
{
  [TestCase(TestName = "FindPath routes around blocked cells")]
  public void FindPathRoutesAroundBlockedCells()
  {
    BattleBoardState board = new(new Vector3I(3, 1, 3));
    board.GetTile(new Vector3I(1, 0, 1)).IsWalkable = false;

    Vector3I[] path = board.FindPath(new Vector3I(0, 0, 1), new Vector3I(2, 0, 1));

    Assert.Equal(new Vector3I(0, 0, 1), path[0]);
    Assert.Equal(new Vector3I(2, 0, 1), path[^1]);
    Assert.Equal(5, path.Length);
    Assert.False(path.Contains(new Vector3I(1, 0, 1)));
  }

  [TestCase(TestName = "FindPath returns an empty path when no traversable route exists")]
  public void FindPathReturnsAnEmptyPathWhenNoTraversableRouteExists()
  {
    BattleBoardState board = new(new Vector3I(3, 1, 3));
    board.GetTile(new Vector3I(1, 0, 0)).IsWalkable = false;
    board.GetTile(new Vector3I(1, 0, 1)).IsWalkable = false;
    board.GetTile(new Vector3I(1, 0, 2)).IsWalkable = false;

    Vector3I[] path = board.FindPath(new Vector3I(0, 0, 1), new Vector3I(2, 0, 1));

    Assert.Equal(0, path.Length);
  }

  [TestCase(TestName = "FindPath stays in sync with occupant changes on the cached graph")]
  public void FindPathStaysInSyncWithOccupantChangesOnTheCachedGraph()
  {
    BattleBoardState board = new(new Vector3I(3, 1, 3));

    Assert.True(board.TryPlaceOccupant(new Vector3I(1, 0, 1), 7));

    Vector3I[] pathAroundOccupant = board.FindPath(new Vector3I(0, 0, 1), new Vector3I(2, 0, 1));
    Vector3I[] pathFromOccupiedSource = board.FindPath(new Vector3I(1, 0, 1), new Vector3I(2, 0, 1), 7);

    Assert.Equal(5, pathAroundOccupant.Length);
    Assert.False(pathAroundOccupant.Contains(new Vector3I(1, 0, 1)));
    Assert.Equal(2, pathFromOccupiedSource.Length);
    Assert.Equal(new Vector3I(1, 0, 1), pathFromOccupiedSource[0]);
    Assert.Equal(new Vector3I(2, 0, 1), pathFromOccupiedSource[^1]);

    Assert.True(board.TryClearOccupant(new Vector3I(1, 0, 1), 7));

    Vector3I[] pathAfterClearingOccupant = board.FindPath(new Vector3I(0, 0, 1), new Vector3I(2, 0, 1));
    Assert.Equal(3, pathAfterClearingOccupant.Length);
  }

  [TestCase(TestName = "TryMoveOccupant moves only the matching unit")]
  public void TryMoveOccupantMovesOnlyTheMatchingUnit()
  {
    BattleBoardState board = new(new Vector3I(3, 1, 3));

    Assert.True(board.TryPlaceOccupant(new Vector3I(0, 0, 0), 7));
    Assert.False(board.TryMoveOccupant(new Vector3I(0, 0, 0), new Vector3I(1, 0, 0), 8));
    Assert.True(board.TryMoveOccupant(new Vector3I(0, 0, 0), new Vector3I(1, 0, 0), 7));
    Assert.False(board.GetTile(new Vector3I(0, 0, 0)).IsOccupied);
    Assert.True(board.GetTile(new Vector3I(1, 0, 0)).OccupantUnitId != null);
    Assert.Equal(7, board.GetTile(new Vector3I(1, 0, 0)).OccupantUnitId!.Value);
  }
}
