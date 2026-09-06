using System;
using Godot;

namespace FunProject.Progression;

// The shared rank ladder for combatants without an authored bespoke table: loads
// resources/ranks.tres once and caches it (same pattern as SkillPathCatalog). Test data
// carries its own ladder (see TestData.MakeRankTable) — the test project's separate
// res:// root has no resources/ directory, so the default load must throw there.
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
