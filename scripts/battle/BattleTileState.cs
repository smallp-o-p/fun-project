namespace FunProject.Battle;

public sealed class BattleTileState
{
  // Walkability is authored at board construction and otherwise mutated only through
  // BattleBoardState.SetTileWalkable.
  public bool IsWalkable { get; internal set; } = true;

  public bool BlocksLineOfSight { get; set; }

  public bool BlocksVerticalLineOfSight { get; set; }

  public TileCover Cover { get; set; } = TileCover.None;
}
