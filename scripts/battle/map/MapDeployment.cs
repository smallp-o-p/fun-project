using System;
using System.Collections.Generic;

namespace FunProject.Battle;

// Pure: turns a baked map's spawn cells (tiles whose SpawnFactionSlot >= 0) + one slot's
// loadouts into spawn placements. SpawnFactionSlot is the index into the battle's side order.
// Pairing decides capacity and order only; bounds, walkability, and collisions are checked
// against the fresh board during BattleFactory.Start.
public static class MapDeployment
{
  public static Either<BattleSetupFailure, IReadOnlyList<UnitPlacement>> AssignSpawns(
    BattleMapData map, int slot, IReadOnlyList<UnitLoadout> loadouts)
  {
    ArgumentNullException.ThrowIfNull(map);
    ArgumentNullException.ThrowIfNull(loadouts);
    ArgumentOutOfRangeException.ThrowIfNegative(slot);
    List<Vector3I> cells = SpawnCellsForSlot(map, slot);
    if (cells.Count < loadouts.Count)
      return Left<BattleSetupFailure, IReadOnlyList<UnitPlacement>>(new(
        BattleSetupFailureReason.SpawnSlotShortfall,
        $"Spawn slot {slot} has {cells.Count} cells but {loadouts.Count} are needed."));
    var placements = new List<UnitPlacement>(loadouts.Count);
    for (int i = 0; i < loadouts.Count; i++)
      placements.Add(new UnitPlacement(loadouts[i], cells[i]));
    return Right<BattleSetupFailure, IReadOnlyList<UnitPlacement>>(placements);
  }

  // Cells whose tile is tagged with the given spawn slot, in a deterministic (X, Y, Z) order so
  // loadouts[i] -> cell[i] assignment is stable. Godot.Vector3I keys (the authored map's
  // coordinate type) are converted to the runtime Vector3I as they are read off the map.
  private static List<Vector3I> SpawnCellsForSlot(BattleMapData map, int slot)
  {
    var cells = new List<Vector3I>();
    foreach (var entry in map.Tiles)
    {
      if (entry.Value is not null && entry.Value.SpawnFactionSlot == slot)
        cells.Add(new Vector3I(entry.Key.X, entry.Key.Y, entry.Key.Z));
    }

    cells.Sort(CompareCells);
    return cells;
  }

  private static int CompareCells(Vector3I a, Vector3I b)
  {
    int cmp = a.X.CompareTo(b.X);
    if (cmp != 0)
      return cmp;
    cmp = a.Y.CompareTo(b.Y);
    if (cmp != 0)
      return cmp;
    return a.Z.CompareTo(b.Z);
  }
}
