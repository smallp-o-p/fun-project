using Godot;

namespace FunProject.Models;

/// <summary>
/// Authored per-mesh mask configuration: mask names with defaults, default
/// bits and mesh, geometry hash, and per-surface triangle flags. Immutable
/// after first use: the shared variant cache keys on configuration identity.
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
  /// <summary>Import-time proof that the fixed masks describe this source geometry.</summary>
  public string ValidateImportGeometry(ArrayMesh? mesh)
  {
    if (mesh is null)
      return "Missing mask source mesh.";
    if (Masks is null || TriangleMasks is null)
      return "Missing mask names or triangle flags.";
    if (Masks.Count > 64)
      return "The mask bitset holds at most 64 masks.";
    long validBits = Masks.Count == 64 ? -1L : (1L << Masks.Count) - 1;
    if ((DefaultBits & ~validBits) != 0)
      return "Mask default bits select unconfigured masks.";
    foreach (long[] flags in TriangleMasks)
    {
      if (flags is null)
        return "Missing surface triangle flags.";
      foreach (long flag in flags)
      {
        if ((flag & ~validBits) != 0)
          return "A triangle flag selects unconfigured masks.";
      }
    }
    if (ModelMaskGeometry.GeometryHash(mesh) != GeometryHash)
      return "Mask geometry hash does not match the source mesh.";
    if (TriangleMasks.Count != mesh.GetSurfaceCount())
      return "Mask surface count does not match the source mesh.";
    for (int surface = 0; surface < TriangleMasks.Count; surface++)
    {
      var arrays = mesh.SurfaceGetArrays(surface);
      int[] indices = arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
      if (indices.Length % 3 != 0 || TriangleMasks[surface].Length != indices.Length / 3)
        return $"Mask triangle count does not match source surface {surface}.";
    }
    return string.Empty;
  }
}
