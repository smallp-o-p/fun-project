#nullable enable
using Godot;
using System;

namespace FunProject.Battle;

public sealed class BattleTileState
{
  private bool _isWalkable = true;

  public Vector3I Coordinates { get; }
  public bool IsWalkable
  {
    get => _isWalkable;
    set
    {
      if (_isWalkable == value)
        return;

      _isWalkable = value;
      TraversalStateChanged?.Invoke(this);
    }
  }
  public bool BlocksLineOfSight { get; set; }
  public bool HasHazard { get; set; }
  public int? OccupantUnitId { get; private set; }
  public bool IsOccupied => OccupantUnitId.HasValue;

  public event Action<BattleTileState>? TraversalStateChanged;

  public BattleTileState(Vector3I coordinates)
  {
    Coordinates = coordinates;
  }

  public bool TrySetOccupant(int unitId)
  {
    if (OccupantUnitId.HasValue || !IsWalkable)
      return false;

    OccupantUnitId = unitId;
    TraversalStateChanged?.Invoke(this);
    return true;
  }

  public void ClearOccupant()
  {
    if (!OccupantUnitId.HasValue)
      return;

    OccupantUnitId = null;
    TraversalStateChanged?.Invoke(this);
  }
}
