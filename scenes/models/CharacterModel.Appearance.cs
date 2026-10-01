using System;
using Godot;

namespace FunProject.Models;

/// <summary>
/// Feeds scene-local face materials with animated world axes. The importer bakes
/// surface/skeleton paths, head indices, and inverse rests; runtime only binds
/// those exact instance slots and samples the changing pose.
/// </summary>
public partial class CharacterModel
{
  public const string DefaultMeshRootName = "Model";
  public const string FaceBoneName = "Head";
  public const string FaceLightingMetadata = "face_lighting";
  public const string UseFaceSdfParameter = "use_face_sdf";
  public const string HeadForwardParameter = "head_forward_world";
  public const string HeadRightParameter = "head_right_world";

  /// <summary>
  /// Imported mesh container carrying baked face_lighting metadata. An empty
  /// path leaves appearance unconfigured; a missing authored path is an error.
  /// </summary>
  [Export] public NodePath MeshRoot { get; set; } = new(DefaultMeshRootName);

  private sealed class FaceLighting
  {
    internal required ShaderMaterial Material { get; init; }
    internal required Skeleton3D Skeleton { get; init; }
    internal required int Head { get; init; }
    internal required Basis InverseGlobalRest { get; init; }
    internal Basis? Last { get; set; }
  }

  private readonly SysColGeneric.List<FaceLighting> _faces = new();

  private void InitializeAppearance()
  {
    _faces.Clear();
    if (!MeshRoot.IsEmpty)
    {
      Node meshRoot = GetNodeOrNull(MeshRoot)
        ?? throw new InvalidOperationException(
          $"The character model '{Name}' mesh root path '{MeshRoot}' does not resolve.");
      if (!meshRoot.HasMeta(FaceLightingMetadata))
        throw new InvalidOperationException(
          $"The character model '{Name}' mesh root has no baked face lighting; reimport or author its bindings.");
      foreach (Godot.Collections.Dictionary binding in meshRoot.GetMeta(FaceLightingMetadata).AsGodotArray())
      {
        NodePath meshPath = binding["mesh_path"].AsNodePath();
        var mesh = meshRoot.GetNode<MeshInstance3D>(meshPath);
        _faces.Add(new FaceLighting
        {
          Material = mesh.GetSurfaceOverrideMaterial(binding["surface_index"].AsInt32()) as ShaderMaterial
            ?? throw new InvalidOperationException($"The character model '{Name}' face binding '{meshPath}' has no shader material."),
          Skeleton = meshRoot.GetNode<Skeleton3D>(binding["skeleton_path"].AsNodePath()),
          Head = binding["head_bone"].AsInt32(),
          InverseGlobalRest = binding["inverse_rest"].AsBasis(),
        });
      }
    }
    if (IsNodeReady())
      UpdateFaceAxes();
    SetProcess(_faces.Count > 0);
  }

  public void UpdateFaceAxes()
  {
    foreach (FaceLighting face in _faces)
    {
      // A reimport that replaced the subtree can free tracked entries between
      // frames; skip them instead of touching dead native objects.
      if (!GodotObject.IsInstanceValid(face.Skeleton) || !GodotObject.IsInstanceValid(face.Material))
        continue;
      Basis axes = face.Skeleton.GlobalBasis
        * face.Skeleton.GetBoneGlobalPose(face.Head).Basis
        * face.InverseGlobalRest;
      if (axes == face.Last)
        continue;
      face.Material.SetShaderParameter(HeadForwardParameter, (axes * Vector3.Back).Normalized());
      face.Material.SetShaderParameter(HeadRightParameter, (axes * Vector3.Right).Normalized());
      face.Last = axes;
    }
  }
}
