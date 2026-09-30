using Godot;

namespace FunProject.Models;

/// <summary>
/// Material isolation: duplicates each surface override material and its
/// outline NextPass once per model instance, while shader and texture
/// subresources stay shared; <c>ResourceLocalToScene</c> marks the copies so
/// scene duplication isolates them too.
/// </summary>
public static class ModelMaterials
{
  internal static void Isolate(Node meshRoot)
  {
    foreach (Node node in meshRoot.FindChildren("*", "MeshInstance3D", true, false))
    {
      var mesh = (MeshInstance3D)node;
      if (mesh.Mesh is null)
        continue;
      for (int surface = 0; surface < mesh.Mesh.GetSurfaceCount(); surface++)
      {
        Material? material = mesh.GetSurfaceOverrideMaterial(surface);
        if (material is null)
          continue;
        mesh.SetSurfaceOverrideMaterial(surface, IsolateMaterial(material));
      }
    }
  }

  private static Material IsolateMaterial(Material material)
  {
    Material isolated = (Material)material.Duplicate();
    isolated.ResourceLocalToScene = true;
    if (isolated.NextPass is not ShaderMaterial outline)
      return isolated;
    ShaderMaterial isolatedOutline = (ShaderMaterial)outline.Duplicate();
    isolatedOutline.ResourceLocalToScene = true;
    isolated.NextPass = isolatedOutline;
    return isolated;
  }
}
