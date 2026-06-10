using FunProject.Battle;
using FunProject.Tests;
using GdUnit4;
using Godot;
using System.Linq;

[TestSuite]
[RequireGodotRuntime]
public partial class BattleBoardStateTest
{
  [TestCase(TestName = "ValidatePoint returns a validated point for in bounds coordinates")]
  public void ValidatePointReturnsAValidatedPointForInBoundsCoordinates()
  {
    BattleBoardState board = new(new Vector3I(3, 2, 4));
    Vector3I coordinates = new(2, 1, 3);

    Option<BattleBoardState.ValidatedPoint> point = board.ValidatePoint(coordinates);

    Assert.True(point.IsSome);
    Assert.Equal(coordinates, point.RequireSome().Raw);
  }

  [TestCase(TestName = "ValidatePoint returns None for out of bounds coordinates")]
  public void ValidatePointReturnsNoneForOutOfBoundsCoordinates()
  {
    BattleBoardState board = new(new Vector3I(3, 2, 4));

    Assert.True(board.ValidatePoint(new Vector3I(-1, 0, 0)).IsNone);
    Assert.True(board.ValidatePoint(new Vector3I(3, 0, 0)).IsNone);
    Assert.True(board.ValidatePoint(new Vector3I(0, 2, 0)).IsNone);
    Assert.True(board.ValidatePoint(new Vector3I(0, 0, 4)).IsNone);
  }

  [TestCase(TestName = "FindPath routes around blocked cells")]
  public void FindPathRoutesAroundBlockedCells()
  {
    BattleBoardState board = new(new Vector3I(3, 1, 3));
    board.GetTile(board.ValidatePoint(new Vector3I(1, 0, 1)).RequireSome()).IsWalkable = false;

    BattleBoardState.ValidatedPoint[] path = board.FindPath(
      board.ValidatePoint(new Vector3I(0, 0, 1)).RequireSome(),
      board.ValidatePoint(new Vector3I(2, 0, 1)).RequireSome(),
      -1);

    Assert.Equal(new Vector3I(0, 0, 1), path[0].Raw);
    Assert.Equal(new Vector3I(2, 0, 1), path[^1].Raw);
    Assert.Equal(5, path.Length);
    Assert.False(path.Any(point => point.Raw == new Vector3I(1, 0, 1)));
  }

  [TestCase(TestName = "FindPath returns an empty path when no traversable route exists")]
  public void FindPathReturnsAnEmptyPathWhenNoTraversableRouteExists()
  {
    BattleBoardState board = new(new Vector3I(3, 1, 3));
    board.GetTile(board.ValidatePoint(new Vector3I(1, 0, 0)).RequireSome()).IsWalkable = false;
    board.GetTile(board.ValidatePoint(new Vector3I(1, 0, 1)).RequireSome()).IsWalkable = false;
    board.GetTile(board.ValidatePoint(new Vector3I(1, 0, 2)).RequireSome()).IsWalkable = false;

    BattleBoardState.ValidatedPoint[] path = board.FindPath(
      board.ValidatePoint(new Vector3I(0, 0, 1)).RequireSome(),
      board.ValidatePoint(new Vector3I(2, 0, 1)).RequireSome(),
      -1);

    Assert.Equal(0, path.Length);
  }

  [TestCase(TestName = "FindPath stays in sync with occupant changes on the cached graph")]
  public void FindPathStaysInSyncWithOccupantChangesOnTheCachedGraph()
  {
    BattleBoardState board = new(new Vector3I(3, 1, 3));

    Assert.True(board.TryPlaceOccupant(board.ValidatePoint(new Vector3I(1, 0, 1)).RequireSome(), 7));

    BattleBoardState.ValidatedPoint[] pathAroundOccupant = board.FindPath(
      board.ValidatePoint(new Vector3I(0, 0, 1)).RequireSome(),
      board.ValidatePoint(new Vector3I(2, 0, 1)).RequireSome(),
      -1);
    BattleBoardState.ValidatedPoint[] pathFromOccupiedSource = board.FindPath(
      board.ValidatePoint(new Vector3I(1, 0, 1)).RequireSome(),
      board.ValidatePoint(new Vector3I(2, 0, 1)).RequireSome(),
      7);

    Assert.Equal(5, pathAroundOccupant.Length);
    Assert.False(pathAroundOccupant.Any(point => point.Raw == new Vector3I(1, 0, 1)));
    Assert.Equal(2, pathFromOccupiedSource.Length);
    Assert.Equal(new Vector3I(1, 0, 1), pathFromOccupiedSource[0].Raw);
    Assert.Equal(new Vector3I(2, 0, 1), pathFromOccupiedSource[^1].Raw);

    Assert.True(board.TryClearOccupant(board.ValidatePoint(new Vector3I(1, 0, 1)).RequireSome(), 7));

    BattleBoardState.ValidatedPoint[] pathAfterClearingOccupant = board.FindPath(
      board.ValidatePoint(new Vector3I(0, 0, 1)).RequireSome(),
      board.ValidatePoint(new Vector3I(2, 0, 1)).RequireSome(),
      -1);
    Assert.Equal(3, pathAfterClearingOccupant.Length);
  }

  [TestCase(TestName = "TryMoveOccupant moves only the matching unit")]
  public void TryMoveOccupantMovesOnlyTheMatchingUnit()
  {
    BattleBoardState board = new(new Vector3I(3, 1, 3));

    Assert.True(board.TryPlaceOccupant(board.ValidatePoint(new Vector3I(0, 0, 0)).RequireSome(), 7));
    Assert.False(board.TryMoveOccupant(
      board.ValidatePoint(new Vector3I(0, 0, 0)).RequireSome(),
      board.ValidatePoint(new Vector3I(1, 0, 0)).RequireSome(),
      8));
    Assert.True(board.TryMoveOccupant(
      board.ValidatePoint(new Vector3I(0, 0, 0)).RequireSome(),
      board.ValidatePoint(new Vector3I(1, 0, 0)).RequireSome(),
      7));
    Assert.False(board.IsOccupied(board.ValidatePoint(new Vector3I(0, 0, 0)).RequireSome()));
    Assert.True(board.GetOccupant(board.ValidatePoint(new Vector3I(1, 0, 0)).RequireSome()).IsSome);
    Assert.Equal(7, board.GetOccupant(board.ValidatePoint(new Vector3I(1, 0, 0)).RequireSome()).RequireSome());
  }
}
