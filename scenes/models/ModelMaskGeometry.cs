using System;
using Godot;

namespace FunProject.Models;

/// <summary>
/// Stateless mask geometry generation: SHA-256 source-mesh hashing, per-bit
/// triangle filtering with per-vertex channel compaction, and outline weight
/// textures. Generated meshes are immutable; callers share them through the
/// mask's variant cache.
/// </summary>
internal static class ModelMaskGeometry
{
  /// <summary>Evict the oldest cached variant once a configuration caches more
  /// than this many.</summary>
  internal const int GeneratedVariantCacheBound = 2;

  /// <summary>Row width of the outline vertex-weight texture.</summary>
  internal const int WeightTextureWidth = 256;

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

  /// <summary>
  /// Builds one mask variant: for every surface, triangles whose flag does not
  /// intersect the active bits survive; their vertices compact per channel with
  /// the source stride, and the surviving source vertex indices are recorded under
  /// "source_vertex_indices" for outline weight remapping.
  /// </summary>
  internal static ArrayMesh BuildMesh(ArrayMesh full, SysColGeneric.IReadOnlyList<long[]> triangleMasks, long bits)
  {
    var result = new ArrayMesh { BlendShapeMode = full.GetBlendShapeMode() };
    for (int i = 0; i < full.GetBlendShapeCount(); i++)
      result.AddBlendShape(full.GetBlendShapeName(i));
    var sourceVertices = new Godot.Collections.Array();
    for (int s = 0; s < full.GetSurfaceCount(); s++)
    {
      Godot.Collections.Array arrays = full.SurfaceGetArrays(s);
      int[] originalIndices = arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
      long[] surfaceFlags = triangleMasks[s];
      var filtered = new SysColGeneric.List<int>();
      for (int t = 0; t < surfaceFlags.Length; t++)
      {
        if ((surfaceFlags[t] & bits) != 0)
          continue;
        for (int c = 0; c < 3; c++)
          filtered.Add(originalIndices[t * 3 + c]);
      }

      // An all-hidden surface emits one degenerate triangle on vertex 0.
      int[] indices = filtered.Count == 0 ? [0, 0, 0] : filtered.ToArray();
      Vector3[] vertices = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
      var remap = new int[vertices.Length];
      System.Array.Fill(remap, -1);
      var kept = new SysColGeneric.List<int>();
      for (int i = 0; i < indices.Length; i++)
      {
        int index = indices[i];
        if (remap[index] < 0)
        {
          remap[index] = kept.Count;
          kept.Add(index);
        }

        indices[i] = remap[index];
      }

      arrays = CompactArrays(arrays, kept, vertices.Length);
      arrays[(int)Mesh.ArrayType.Index] = indices;
      var shapes = new Godot.Collections.Array<Godot.Collections.Array>();
      foreach (Godot.Collections.Array shape in full.SurfaceGetBlendShapeArrays(s))
        shapes.Add(CompactArrays(shape, kept, vertices.Length));
      result.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays, blendShapes: shapes,
        flags: (Mesh.ArrayFormat)full.SurfaceGetFormat(s));
      result.SurfaceSetMaterial(s, full.SurfaceGetMaterial(s));
      result.SurfaceSetName(s, full.SurfaceGetName(s));
      sourceVertices.Add(kept.ToArray());
    }

    result.SetMeta("source_vertex_indices", sourceVertices);
    return result;
  }

  /// <summary>Outline vertex weights as a 256-wide, one-float-per-texel texture
  /// with as many rows as the values need.</summary>
  internal static ImageTexture WeightTexture(float[] values)
  {
    int rows = (values.Length + WeightTextureWidth - 1) / WeightTextureWidth;
    var bytes = new byte[rows * WeightTextureWidth * sizeof(float)];
    Buffer.BlockCopy(values, 0, bytes, 0, values.Length * sizeof(float));
    return ImageTexture.CreateFromImage(
      Godot.Image.CreateFromData(WeightTextureWidth, rows, false, Godot.Image.Format.Rf, bytes));
  }

  private static Godot.Collections.Array CompactArrays(Godot.Collections.Array source,
    SysColGeneric.IReadOnlyList<int> kept, int vertexCount)
  {
    Godot.Collections.Array result = source.Duplicate();
    for (int a = 0; a < (int)Mesh.ArrayType.Max; a++)
    {
      if (a == (int)Mesh.ArrayType.Index)
        continue;
      Variant channel = source[a];
      if (channel.VariantType == Variant.Type.Nil)
        continue;
      result[a] = CompactChannel(channel, kept, vertexCount);
    }

    return result;
  }

  private static Variant CompactChannel(Variant channel, SysColGeneric.IReadOnlyList<int> kept, int vertexCount)
    => channel.VariantType switch
    {
      Variant.Type.PackedVector3Array => CopyChannel(channel.AsVector3Array(), kept, vertexCount),
      Variant.Type.PackedVector2Array => CopyChannel(channel.AsVector2Array(), kept, vertexCount),
      Variant.Type.PackedColorArray => CopyChannel(channel.AsColorArray(), kept, vertexCount),
      Variant.Type.PackedFloat32Array => CopyChannel(channel.AsFloat32Array(), kept, vertexCount),
      Variant.Type.PackedInt32Array => CopyChannel(channel.AsInt32Array(), kept, vertexCount),
      Variant.Type.PackedInt64Array => CopyChannel(channel.AsInt64Array(), kept, vertexCount),
      Variant.Type.PackedByteArray => CopyChannel(channel.AsByteArray(), kept, vertexCount),
      _ => throw new InvalidOperationException(
        $"Mask mesh channels of type {channel.VariantType} cannot be compacted."),
    };

  private static Variant CopyChannel<T>(T[] input, SysColGeneric.IReadOnlyList<int> kept, int vertexCount)
  {
    if (input.Length % vertexCount != 0)
      throw new InvalidOperationException(
        $"Mask mesh channel holds {input.Length} entries for {vertexCount} vertices; channels compact per whole vertex.");
    int stride = input.Length / vertexCount;
    var output = new T[kept.Count * stride];
    for (int i = 0; i < kept.Count; i++)
      System.Array.Copy(input, kept[i] * stride, output, i * stride, stride);

    return Variant.From(output);
  }
}
