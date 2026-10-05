using System;
using Godot;

namespace FunProject.Models;

/// <summary>Validation at the enforced Blender post-import boundary, never on model instances.</summary>
[Tool]
public partial class ModelImportValidation : RefCounted
{
  /// <summary>Import-time proof that the fixed masks describe this source geometry.</summary>
  public string ValidateMaskGeometry(ModelMaskConfiguration configuration, ArrayMesh? mesh)
  {
    if (mesh is null)
      return "Missing mask source mesh.";
    if (configuration.Masks is null || configuration.TriangleMasks is null)
      return "Missing mask names or triangle flags.";
    if (configuration.Masks.Count > 64)
      return "The mask bitset holds at most 64 masks.";
    long validBits = configuration.Masks.Count == 64 ? -1L : (1L << configuration.Masks.Count) - 1;
    if ((configuration.DefaultBits & ~validBits) != 0)
      return "Mask default bits select unconfigured masks.";
    foreach (long[] flags in configuration.TriangleMasks)
    {
      if (flags is null)
        return "Missing surface triangle flags.";
      foreach (long flag in flags)
      {
        if ((flag & ~validBits) != 0)
          return "A triangle flag selects unconfigured masks.";
      }
    }
    string geometryError = ValidateSourceGeometry(mesh);
    if (geometryError.Length != 0)
      return geometryError;
    if (GeometryHash(mesh) != configuration.GeometryHash)
      return "Mask geometry hash does not match the source mesh.";
    if (configuration.TriangleMasks.Count != mesh.GetSurfaceCount())
      return "Mask surface count does not match the source mesh.";
    for (int surface = 0; surface < configuration.TriangleMasks.Count; surface++)
    {
      var arrays = mesh.SurfaceGetArrays(surface);
      int[] indices = arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
      if (indices.Length % 3 != 0 || configuration.TriangleMasks[surface].Length != indices.Length / 3)
        return $"Mask triangle count does not match source surface {surface}.";
    }
    if (configuration.DefaultMesh is not null)
    {
      string defaultError = ValidateSourceGeometry(configuration.DefaultMesh);
      if (defaultError.Length != 0)
        return "Default mesh: " + defaultError;
      using ArrayMesh expected = ModelMaskGeometry.BuildMesh(mesh, configuration.TriangleMasks, configuration.DefaultBits);
      if (GeometryHash(configuration.DefaultMesh) != GeometryHash(expected))
        return "Default mask mesh does not match its source geometry and bits.";
      if (!configuration.DefaultMesh.HasMeta("source_vertex_indices")
        || !GD.VarToBytes(configuration.DefaultMesh.GetMeta("source_vertex_indices")).AsSpan()
          .SequenceEqual(GD.VarToBytes(expected.GetMeta("source_vertex_indices"))))
        return "Default mask mesh has invalid source vertex remapping.";
    }
    return string.Empty;
  }

  public string ValidateMaskPresentation(MeshInstance3D body)
  {
    for (int surface = 0; surface < body.Mesh.GetSurfaceCount(); surface++)
    {
      Material? material = body.GetSurfaceOverrideMaterial(surface);
      // Outlines/weights are optional, but configured weights are fixed data.
      if (material?.NextPass is not ShaderMaterial outline || !outline.HasMeta("source_weights"))
        continue;
      Variant weights = outline.GetMeta("source_weights");
      int vertices = body.Mesh.SurfaceGetArrays(surface)[(int)Mesh.ArrayType.Vertex].AsVector3Array().Length;
      if (weights.VariantType != Variant.Type.PackedFloat32Array || weights.AsFloat32Array().Length != vertices)
        return $"Mask outline {surface} source weights must match the source vertices.";
      if (!material.ResourceLocalToScene || !outline.ResourceLocalToScene)
        return $"Mask outline {surface} and its surface material must be scene-local.";
    }
    return string.Empty;
  }

  // Runs only in the importer, before either default or runtime variants can
  // use the compacting algorithm. Godot already checks channel slot semantics.
  private static string ValidateSourceGeometry(ArrayMesh mesh)
  {
    if (mesh.GetSurfaceCount() == 0)
      return "Mask source mesh has no surfaces.";
    for (int surface = 0; surface < mesh.GetSurfaceCount(); surface++)
    {
      if (mesh.SurfaceGetPrimitiveType(surface) != Mesh.PrimitiveType.Triangles)
        return "Mask source surfaces must contain triangles.";
      var arrays = mesh.SurfaceGetArrays(surface);
      int vertices = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array().Length;
      if (vertices == 0)
        return "Mask source surface has no vertices.";
      int[] indices = arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
      if (indices.Length == 0 || indices.Length % 3 != 0)
        return "Mask source surface requires indexed triangles.";
      foreach (int index in indices)
      {
        if (index < 0 || index >= vertices)
          return "Mask source index is outside its vertex array.";
      }
      string error = ValidateChannels(arrays, vertices);
      if (error.Length != 0)
        return error;
      var shapes = mesh.SurfaceGetBlendShapeArrays(surface);
      if (shapes.Count != mesh.GetBlendShapeCount())
        return "Mask source blend shape count does not match its channels.";
      foreach (Godot.Collections.Array shape in shapes)
      {
        error = ValidateChannels(shape, vertices);
        if (error.Length != 0)
          return error;
      }
    }
    return string.Empty;
  }

  private static string ValidateChannels(Godot.Collections.Array arrays, int vertices)
  {
    for (int slot = 0; slot < (int)Mesh.ArrayType.Max; slot++)
    {
      if (slot == (int)Mesh.ArrayType.Index)
        continue;
      Variant channel = arrays[slot];
      int length = channel.VariantType switch
      {
        Variant.Type.Nil => 0,
        Variant.Type.PackedVector3Array => channel.AsVector3Array().Length,
        Variant.Type.PackedVector2Array => channel.AsVector2Array().Length,
        Variant.Type.PackedColorArray => channel.AsColorArray().Length,
        Variant.Type.PackedFloat32Array => channel.AsFloat32Array().Length,
        Variant.Type.PackedInt32Array => channel.AsInt32Array().Length,
        Variant.Type.PackedInt64Array => channel.AsInt64Array().Length,
        Variant.Type.PackedByteArray => channel.AsByteArray().Length,
        _ => -1,
      };
      if (length < 0 || length % vertices != 0)
        return $"Mask channel {slot} must contain supported whole-vertex data.";
    }
    return string.Empty;
  }

  /// <summary>
  /// Hash of everything the mask geometry depends on: blend shape mode and names,
  /// every surface's arrays, blend shape arrays, and material names.
  /// </summary>
  internal static string GeometryHash(ArrayMesh mesh)
  {
    var context = new HashingContext();
    context.Start(HashingContext.HashType.Sha256);
    context.Update(GD.VarToBytes(Variant.From(mesh.GetBlendShapeMode())));
    for (int i = 0; i < mesh.GetBlendShapeCount(); i++)
      context.Update(GD.VarToBytes(Variant.From(mesh.GetBlendShapeName(i))));
    for (int s = 0; s < mesh.GetSurfaceCount(); s++)
    {
      context.Update(GD.VarToBytes(mesh.SurfaceGetArrays(s)));
      context.Update(GD.VarToBytes(mesh.SurfaceGetBlendShapeArrays(s)));
      Material? material = mesh.SurfaceGetMaterial(s);
      context.Update(GD.VarToBytes(Variant.From(material?.ResourceName ?? string.Empty)));
    }

    return Convert.ToHexString(context.Finish()).ToLowerInvariant();
  }

}
