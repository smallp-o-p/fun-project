using FunProject.Battle;
using GdUnit4;
using Godot;

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
    board.SetTileWalkable(board.At(1, 0, 1), false);
    Assert.True(board.TryPlaceOccupant(board.At(0, 0, 1), 1));

    BattleBoardState.ValidatedPoint[] path = board.FindPath(1, board.At(2, 0, 1));

    Assert.Equal(new Vector3I(0, 0, 1), path[0].Raw);
    Assert.Equal(new Vector3I(2, 0, 1), path[^1].Raw);
    Assert.Equal(5, path.Length);
    Assert.False(path.AsValueEnumerable().Any(point => point.Raw == new Vector3I(1, 0, 1)));
  }

  [TestCase(TestName = "FindPath returns an empty path when no traversable route exists")]
  public void FindPathReturnsAnEmptyPathWhenNoTraversableRouteExists()
  {
    BattleBoardState board = new(new Vector3I(3, 1, 3));
    board.SetTileWalkable(board.At(1, 0, 0), false);
    board.SetTileWalkable(board.At(1, 0, 1), false);
    board.SetTileWalkable(board.At(1, 0, 2), false);
    Assert.True(board.TryPlaceOccupant(board.At(0, 0, 1), 1));

    BattleBoardState.ValidatedPoint[] path = board.FindPath(1, board.At(2, 0, 1));

    Assert.Equal(0, path.Length);
  }

  [TestCase(TestName = "FindPath returns an empty path for a unit not placed on the board")]
  public void FindPathReturnsAnEmptyPathForAUnitNotPlacedOnTheBoard()
  {
    BattleBoardState board = new(new Vector3I(3, 1, 3));

    BattleBoardState.ValidatedPoint[] path = board.FindPath(42, board.At(2, 0, 1));

    Assert.Equal(0, path.Length);
  }

  [TestCase(TestName = "FindPath stays in sync with occupant changes on the cached graph")]
  public void FindPathStaysInSyncWithOccupantChangesOnTheCachedGraph()
  {
    BattleBoardState board = new(new Vector3I(3, 1, 3));

    Assert.True(board.TryPlaceOccupant(board.At(1, 0, 1), 7));
    Assert.True(board.TryPlaceOccupant(board.At(0, 0, 1), 2));

    BattleBoardState.ValidatedPoint[] pathAroundOccupant = board.FindPath(2, board.At(2, 0, 1));
    BattleBoardState.ValidatedPoint[] pathFromOccupiedSource = board.FindPath(7, board.At(2, 0, 1));

    Assert.Equal(5, pathAroundOccupant.Length);
    Assert.False(pathAroundOccupant.AsValueEnumerable().Any(point => point.Raw == new Vector3I(1, 0, 1)));
    Assert.Equal(2, pathFromOccupiedSource.Length);
    Assert.Equal(new Vector3I(1, 0, 1), pathFromOccupiedSource[0].Raw);
    Assert.Equal(new Vector3I(2, 0, 1), pathFromOccupiedSource[^1].Raw);

    Assert.True(board.TryClearOccupant(board.At(1, 0, 1), 7));

    BattleBoardState.ValidatedPoint[] pathAfterClearingOccupant = board.FindPath(2, board.At(2, 0, 1));
    Assert.Equal(3, pathAfterClearingOccupant.Length);
  }

  [TestCase(TestName = "TryMoveOccupant moves only the matching unit")]
  public void TryMoveOccupantMovesOnlyTheMatchingUnit()
  {
    BattleBoardState board = new(new Vector3I(3, 1, 3));

    Assert.True(board.TryPlaceOccupant(board.At(0, 0, 0), 7));
    Assert.False(board.TryMoveOccupant(
      board.At(0, 0, 0),
      board.At(1, 0, 0),
      8));
    Assert.True(board.TryMoveOccupant(
      board.At(0, 0, 0),
      board.At(1, 0, 0),
      7));
    Assert.False(board.IsOccupied(board.At(0, 0, 0)));
    Assert.True(board.GetOccupant(board.At(1, 0, 0)).IsSome);
    Assert.Equal(7, board.GetOccupant(board.At(1, 0, 0)).RequireSome());
  }
}
