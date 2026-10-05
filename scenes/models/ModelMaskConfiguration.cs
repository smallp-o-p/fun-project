using Godot;

namespace FunProject.Models;

/// <summary>
/// Authored per-mesh mask configuration: mask names with defaults, default
/// bits and mesh, geometry hash, and per-surface triangle flags. Immutable
/// after import: the shared variant cache keys on configuration identity.
/// </summary>
[Tool, GlobalClass]
public partial class ModelMaskConfiguration : Resource
{
  /// <summary>Mask name → default enabled; bit index follows insertion order.</summary>
  [Export] public Godot.Collections.Dictionary<string, bool> Masks { get; set; } = new();

  /// <summary>Bitset the stored <see cref="DefaultMesh"/> renders.</summary>
  [Export] public long DefaultBits { get; set; }

  /// <summary>Prebuilt render mesh for <see cref="DefaultBits"/>.</summary>
  [Export] public ArrayMesh? DefaultMesh { get; set; }

  /// <summary>Geometry hash of the source mesh the triangle masks describe.</summary>
  [Export] public string GeometryHash { get; set; } = "";

  /// <summary>Per-surface triangle membership flags, one long per triangle.</summary>
  [Export] public Godot.Collections.Array<long[]> TriangleMasks { get; set; } = new();
}
