using System;
using System.Text.Json;
using Godot;

namespace FunProject.Models;

/// <summary>Compiles the frozen Blender polygon masks against the actual imported geometry.</summary>
[Tool]
public partial class GraceCrowneMaskImport : RefCounted
{
  public ModelMaskConfiguration Compile(ArrayMesh mesh, string authoredJson)
  {
    using var document = JsonDocument.Parse(authoredJson);
    JsonElement data = document.RootElement;
    var configuration = new ModelMaskConfiguration { DefaultBits = data.GetProperty("default_bits").GetInt64() };
    int bit = 0;
    foreach (JsonElement name in data.GetProperty("names").EnumerateArray())
      configuration.Masks.Add(name.GetString()!, (configuration.DefaultBits & (1L << bit++)) != 0);
    var triangles = new SysColGeneric.Dictionary<string, long>();
    foreach (JsonElement triangle in data.GetProperty("triangles").EnumerateArray())
    {
      var positions = triangle[0];
      string key = TriangleKey(
        Point(positions[0].GetInt32(), positions[1].GetInt32(), positions[2].GetInt32()),
        Point(positions[3].GetInt32(), positions[4].GetInt32(), positions[5].GetInt32()),
        Point(positions[6].GetInt32(), positions[7].GetInt32(), positions[8].GetInt32()));
      long flags = triangle[1].GetInt64();
      if (triangles.TryGetValue(key, out long existing) && existing != flags)
        throw new InvalidOperationException("Ambiguous authored Grace mask triangle.");
      triangles[key] = flags;
    }
    var used = new SysColGeneric.HashSet<string>();
    for (int surface = 0; surface < mesh.GetSurfaceCount(); surface++)
    {
      var arrays = mesh.SurfaceGetArrays(surface);
      Vector3[] vertices = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
      int[] indices = arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
      var flags = new long[indices.Length / 3];
      for (int i = 0; i < indices.Length; i += 3)
      {
        string key = TriangleKey(Point(vertices[indices[i]]), Point(vertices[indices[i + 1]]), Point(vertices[indices[i + 2]]));
        if (!triangles.TryGetValue(key, out flags[i / 3]))
          throw new InvalidOperationException($"Imported Grace geometry differs from its authored masks: surface {surface}, triangle {i / 3}.");
        used.Add(key);
      }
      configuration.TriangleMasks.Add(flags);
    }
    if (used.Count != triangles.Count)
      throw new InvalidOperationException("Imported Grace geometry omits authored mask triangles.");
    configuration.GeometryHash = ModelImportValidation.GeometryHash(mesh);
    configuration.DefaultMesh = ModelMaskGeometry.BuildMesh(mesh, configuration.TriangleMasks, configuration.DefaultBits);
    string error = new ModelImportValidation().ValidateMaskGeometry(configuration, mesh);
    if (error.Length != 0)
      throw new InvalidOperationException(error);
    return configuration;
  }

  private static string Point(Vector3 point) => Point(
    (int)Math.Round(point.X * 10000.0), (int)Math.Round(point.Y * 10000.0), (int)Math.Round(point.Z * 10000.0));

  private static string Point(int x, int y, int z) => $"{x},{y},{z}";

  private static string TriangleKey(string a, string b, string c)
  {
    string[] points = [a, b, c];
    System.Array.Sort(points, StringComparer.Ordinal);
    return string.Join(";", points);
  }
}
