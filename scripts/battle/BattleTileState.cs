namespace FunProject.Battle;

public sealed class BattleTileState
{
  // Walkability is authored at board construction and otherwise mutated only through
  // BattleBoardState.SetTileWalkable.
  public bool IsWalkable { get; internal set; } = true;

  public bool BlocksLineOfSight { get; internal set; }

  public bool BlocksVerticalLineOfSight { get; internal set; }

  public TileCover Cover { get; internal set; } = TileCover.None;
}
