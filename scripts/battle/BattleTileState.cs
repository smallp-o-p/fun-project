using System;

namespace FunProject.Battle;

public sealed class BattleTileState
{
  private bool _isWalkable = true;
  public bool IsWalkable
  {
    get => _isWalkable;
    set
    {
      if (_isWalkable == value)
        return;

      _isWalkable = value;
      TraversalStateChanged.Invoke(this);
    }
  }

  public bool BlocksLineOfSight { get; set; }

  public TileCover Cover { get; set; } = TileCover.None;

  public event Action<BattleTileState> TraversalStateChanged = delegate { };
}
