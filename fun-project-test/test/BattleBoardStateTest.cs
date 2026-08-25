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

    BattleBoardState.ValidatedPoint[] path = board.FindPath(board.At(0, 0, 1), board.At(2, 0, 1));

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

    BattleBoardState.ValidatedPoint[] path = board.FindPath(board.At(0, 0, 1), board.At(2, 0, 1));

    Assert.Equal(0, path.Length);
  }

  [TestCase(TestName = "FindPath returns an empty path when the destination is occupied")]
  public void FindPathReturnsAnEmptyPathWhenTheDestinationIsOccupied()
  {
    BattleBoardState board = new(new Vector3I(3, 1, 3));
    Assert.True(board.TryPlaceOccupant(board.At(2, 0, 1), 7));

    BattleBoardState.ValidatedPoint[] path = board.FindPath(board.At(0, 0, 1), board.At(2, 0, 1));

    Assert.Equal(0, path.Length);
  }

  [TestCase(TestName = "FindPath breaks equal-length ties by the fixed direction order")]
  public void FindPathBreaksEqualLengthTiesByTheFixedDirectionOrder()
  {
    // From (0,0,0) to (1,0,1) two shortest routes exist — via (1,0,0) or via (0,0,1). The
    // search visits the +X neighbor before the +Z neighbor, so the path runs X-first. This
    // pins the deterministic tie-break that the fixed OrthogonalDirections order provides.
    BattleBoardState board = new(new Vector3I(3, 1, 3));

    BattleBoardState.ValidatedPoint[] path = board.FindPath(board.At(0, 0, 0), board.At(1, 0, 1));

    Assert.Equal(3, path.Length);
    Assert.Equal(new Vector3I(1, 0, 0), path[1].Raw);
  }

  [TestCase(TestName = "FindPath stays in sync with occupant changes")]
  public void FindPathStaysInSyncWithOccupantChanges()
  {
    BattleBoardState board = new(new Vector3I(3, 1, 3));

    Assert.True(board.TryPlaceOccupant(board.At(1, 0, 1), 7));
    Assert.True(board.TryPlaceOccupant(board.At(0, 0, 1), 2));

    BattleBoardState.ValidatedPoint[] pathAroundOccupant = board.FindPath(board.At(0, 0, 1), board.At(2, 0, 1));
    BattleBoardState.ValidatedPoint[] pathFromOccupiedSource = board.FindPath(board.At(1, 0, 1), board.At(2, 0, 1));

    Assert.Equal(5, pathAroundOccupant.Length);
    Assert.False(pathAroundOccupant.AsValueEnumerable().Any(point => point.Raw == new Vector3I(1, 0, 1)));
    Assert.Equal(2, pathFromOccupiedSource.Length);
    Assert.Equal(new Vector3I(1, 0, 1), pathFromOccupiedSource[0].Raw);
    Assert.Equal(new Vector3I(2, 0, 1), pathFromOccupiedSource[^1].Raw);

    Assert.True(board.TryClearOccupant(board.At(1, 0, 1), 7));

    BattleBoardState.ValidatedPoint[] pathAfterClearingOccupant = board.FindPath(board.At(0, 0, 1), board.At(2, 0, 1));
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
