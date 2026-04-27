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
  public bool HasHazard { get; set; }
  public Option<int> OccupantUnitId { get; private set; }
  public bool IsOccupied => OccupantUnitId.IsSome;

  public event Action<BattleTileState> TraversalStateChanged = delegate { };

  public bool TrySetOccupant(int unitId)
  {
    if (OccupantUnitId.IsSome || !IsWalkable)
      return false;

    OccupantUnitId = Some(unitId);
    TraversalStateChanged.Invoke(this);
    return true;
  }

  public bool HasOccupant(int unitId)
  {
    return OccupantUnitId.Match(
      occupantUnitId => occupantUnitId == unitId,
      () => false);
  }

  public void ClearOccupant()
  {
    if (OccupantUnitId.IsNone)
      return;

    OccupantUnitId = None;
    TraversalStateChanged.Invoke(this);
  }
}
