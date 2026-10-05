namespace FunProject.Battle;

public struct BattleTileState
{
  private BattleTileFlags _flags;

  public BattleTileState() => _flags = BattleTileFlags.Walkable;

  // Walkability is authored at board construction and otherwise mutated only through
  // BattleBoardState.SetTileWalkable.
  public bool IsWalkable
  {
    get => (_flags & BattleTileFlags.Walkable) != 0;
    internal set => Set(BattleTileFlags.Walkable, value);
  }

  public bool BlocksLineOfSight
  {
    get => (_flags & BattleTileFlags.BlocksLineOfSight) != 0;
    set => Set(BattleTileFlags.BlocksLineOfSight, value);
  }

  public bool BlocksVerticalLineOfSight
  {
    get => (_flags & BattleTileFlags.BlocksVerticalLineOfSight) != 0;
    set => Set(BattleTileFlags.BlocksVerticalLineOfSight, value);
  }

  public TileCover Cover { get; set; }

  private void Set(BattleTileFlags flag, bool value) => _flags = value ? _flags | flag : _flags & ~flag;
}
