using System;
using System.Collections.Generic;
using FunProject.Progression;
using Godot;

// The skill-path catalog seam: loads the authored resources/skills/catalog.tres once and
// exposes its paths. Authored references pack into exports automatically (no res://
// directory scan, which does not survive export). Order is authored array order.
public static class SkillPathCatalog
{
  private static IReadOnlyList<SkillPathData>? _paths;

  public static IReadOnlyList<SkillPathData> Paths => _paths ??= Load();

  private static IReadOnlyList<SkillPathData> Load()
  {
    var catalog = GD.Load<SkillPathCatalogData>("res://resources/skills/catalog.tres");
    if (catalog is null)
      throw new InvalidOperationException(
        "resources/skills/catalog.tres is missing or failed to load; the skill-path catalog must be authored.");

    var paths = new List<SkillPathData>();
    foreach (SkillPathData path in catalog.Paths)
      paths.Add(path);
    return paths;
  }
}
