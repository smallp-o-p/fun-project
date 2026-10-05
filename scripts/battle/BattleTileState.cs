namespace FunProject.Battle;

/// <summary>A live cell view. Copying this value preserves access to the same board cell.</summary>
public readonly struct BattleTileState
{
  private readonly BattleBoardState _board;
  private readonly int _index;

  internal BattleTileState(BattleBoardState board, int index) { _board = board; _index = index; }

  public bool IsWalkable
  {
    get => Has(BattleTileIndex.Walkable);
    internal set => Set(BattleTileIndex.Walkable, value);
  }

  public bool BlocksLineOfSight
  {
    get => Has(BattleTileIndex.BlocksSight);
    set => Set(BattleTileIndex.BlocksSight, value);
  }

  public bool BlocksVerticalLineOfSight
  {
    get => Has(BattleTileIndex.BlocksVerticalSight);
    set => Set(BattleTileIndex.BlocksVerticalSight, value);
  }

  public TileCover Cover
  {
    get => new((CoverDirections)_board.TileCoverDirections[_index], _board.TileCoverAmounts[_index]);
    set
    {
      // Gameplay uses the authored 0..100 cover domain. Reject unrepresentable values
      // instead of silently wrapping when callers construct an invalid cover value.
      byte directions = checked((byte)value.Directions);
      byte amount = checked((byte)value.Amount);
      _board.TileCoverDirections[_index] = directions;
      _board.TileCoverAmounts[_index] = amount;
    }
  }

  private bool Has(byte mask) => (_board.TileFlags[_index] & mask) != 0;
  private void Set(byte mask, bool value) => _board.TileFlags[_index] = value
    ? (byte)(_board.TileFlags[_index] | mask) : (byte)(_board.TileFlags[_index] & ~mask);
}
