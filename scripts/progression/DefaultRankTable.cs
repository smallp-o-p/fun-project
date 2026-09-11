using System;
using Godot;

namespace FunProject.Progression;

// The shared authored rank ladder for combatants without a bespoke table: loads
// resources/ranks.tres once and caches it (same pattern as SkillPathCatalog). Authors
// give a combatant its own RankTable when it needs a custom ladder; everything else
// resolves through this shared default.
public static class DefaultRankTable
{
  private static RankTableData? _table;

  public static RankTableData Table => _table ??= Load();

  private static RankTableData Load()
  {
    var table = GD.Load<RankTableData>("res://resources/ranks.tres");
    if (table is null)
      throw new InvalidOperationException(
        "resources/ranks.tres is missing or failed to load; the default rank table must be authored.");
    return table;
  }
}
