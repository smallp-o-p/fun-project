using FunProject.Combatants;
using Godot;
using System.Collections.Generic;

namespace FunProject.Battle;

// Pure: turns a baked map's spawn cells (tiles whose SpawnFactionSlot >= 0) + per-slot rosters
// into spawn placements. SpawnFactionSlot is the index into the BattleSession faction order.
// Callers (BattleFactory) validate each (Combatant, Vector3I) against the board before feeding
// it into BattleAction.SpawnUnit, which takes a ValidatedPoint.
public static class MapDeployment
{
  public static Either<string, IReadOnlyList<(Combatant Combatant, Vector3I Position)>> AssignSpawns(
    BattleMapData map,
    IReadOnlyDictionary<int, IReadOnlyList<Combatant>> rostersBySlot)
  {
    var placements = new List<(Combatant Combatant, Vector3I Position)>();

    foreach (KeyValuePair<int, IReadOnlyList<Combatant>> roster in rostersBySlot)
    {
      int slot = roster.Key;
      IReadOnlyList<Combatant> combatants = roster.Value;

      List<Vector3I> cells = SpawnCellsForSlot(map, slot);
      if (cells.Count < combatants.Count)
        return Left<string, IReadOnlyList<(Combatant Combatant, Vector3I Position)>>(
          $"Spawn slot {slot} has {cells.Count} cells but {combatants.Count} are needed.");

      for (int i = 0; i < combatants.Count; i++)
      {
        Vector3I cell = cells[i];
        if (!IsInBounds(cell, map.Dimensions))
          return Left<string, IReadOnlyList<(Combatant Combatant, Vector3I Position)>>(
            $"Spawn cell {cell} for slot {slot} is out of bounds for dimensions {map.Dimensions}.");

        placements.Add((combatants[i], cell));
      }
    }

    IReadOnlyList<(Combatant Combatant, Vector3I Position)> result = placements;
    return Right<string, IReadOnlyList<(Combatant Combatant, Vector3I Position)>>(result);
  }

  // Cells whose tile is tagged with the given spawn slot, in a deterministic (X, Y, Z) order so
  // roster[i] -> cell[i] assignment is stable regardless of dictionary iteration order.
  private static List<Vector3I> SpawnCellsForSlot(BattleMapData map, int slot)
  {
    var cells = new List<Vector3I>();
    foreach (KeyValuePair<Vector3I, BattleMapTileData> entry in map.Tiles)
    {
      if (entry.Value is not null && entry.Value.SpawnFactionSlot == slot)
        cells.Add(entry.Key);
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

  private static bool IsInBounds(Vector3I cell, Vector3I dimensions)
  {
    return cell.X >= 0 && cell.Y >= 0 && cell.Z >= 0
      && cell.X < dimensions.X && cell.Y < dimensions.Y && cell.Z < dimensions.Z;
  }
}
