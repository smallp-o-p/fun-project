using Godot;

namespace FunProject.Battle;

public sealed class BattleTileState
{
  public Vector3I Coordinates { get; }
  public bool IsWalkable { get; set; } = true;
  public bool BlocksLineOfSight { get; set; }
  public bool HasHazard { get; set; }
  public int? OccupantUnitId { get; private set; }
  public bool IsOccupied => OccupantUnitId.HasValue;

  public BattleTileState(Vector3I coordinates)
  {
    Coordinates = coordinates;
  }

  public bool TrySetOccupant(int unitId)
  {
    if (OccupantUnitId.HasValue || !IsWalkable)
      return false;

    OccupantUnitId = unitId;
    return true;
  }

  public void ClearOccupant()
  {
    OccupantUnitId = null;
  }
}
