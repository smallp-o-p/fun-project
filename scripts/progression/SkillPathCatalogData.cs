using Godot;

namespace FunProject.Progression;

/// <summary>
/// Authored skill-path catalog: the export-safe list of paths offered to units. Resources
/// referenced here are packed into exports automatically — no res:// directory scanning
/// (which does not survive export: .tres remaps to .res and DirAccess on res:// is
/// unreliable in packed builds).
/// </summary>
[GlobalClass]
public partial class SkillPathCatalogData : Resource
{
  [Export] public Godot.Collections.Array<SkillPathData> Paths { get; set; } = [];
}
