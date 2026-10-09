namespace FunProject.Battle;

public struct BattleTileState
{
  private BattleTileFlags _flags;

  // Walkability is authored at board construction and otherwise mutated only through
  // BattleBoardState.SetTileWalkable.
  public bool IsWalkable
  {
    readonly get => (_flags & BattleTileFlags.Walkable) != BattleTileFlags.None;
    internal set => Set(BattleTileFlags.Walkable, value);
  }

  public bool BlocksLineOfSight
  {
    readonly get => (_flags & BattleTileFlags.BlocksLineOfSight) != BattleTileFlags.None;
    set => Set(BattleTileFlags.BlocksLineOfSight, value);
  }

  public bool BlocksVerticalLineOfSight
  {
    readonly get => (_flags & BattleTileFlags.BlocksVerticalLineOfSight) != BattleTileFlags.None;
    set => Set(BattleTileFlags.BlocksVerticalLineOfSight, value);
  }

  public TileCover Cover { get; set; }

  private void Set(BattleTileFlags flag, bool value) => _flags = value ? _flags | flag : _flags & ~flag;
}
