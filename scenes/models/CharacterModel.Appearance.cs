using System;
using Godot;

namespace FunProject.Models;

/// <summary>
/// Appearance half of the model root: isolates the surface override materials of
/// the mesh root once per instance and feeds the face SDF materials with
/// head-derived world axes. Outlines are authored material settings carried over
/// by the isolation; this half owns no runtime outline state.
/// </summary>
public partial class CharacterModel
{
  public const string DefaultMeshRootName = "Model";
  public const string FaceBoneName = "head.x";
  public const string UseFaceSdfParameter = "use_face_sdf";
  public const string HeadForwardParameter = "head_forward_world";
  public const string HeadRightParameter = "head_right_world";

  /// <summary>
  /// Mesh container whose MeshInstance3D surfaces are isolated. Resolved against
  /// this root; an empty path leaves the appearance unconfigured while an authored
  /// path that misses is an authoring error.
  /// </summary>
  [Export] public NodePath MeshRoot { get; set; } = new(DefaultMeshRootName);

  /// <summary>The bone driving the face-lighting axes, fixed for the initialized
  /// instance like <see cref="MeshRoot"/>; author it before initialization.</summary>
  [Export] public string HeadBoneName { get; set; } = FaceBoneName;

  private sealed class FaceLighting
  {
    internal required ShaderMaterial Material { get; init; }
    internal required Skeleton3D Skeleton { get; init; }
    internal required int Head { get; init; }
    internal required Basis InverseGlobalRest { get; init; }
    internal Basis? Last { get; set; }
  }

  private readonly SysColGeneric.List<FaceLighting> _faces = new();
  private Node? _meshRoot;
  private bool _isolated;

  /// <summary>
  /// Isolates the mesh root's surface materials once per instance and starts the
  /// face-axis tracking. Repeat calls reuse the isolated materials; a reimport
  /// that replaced the mesh root resets the isolation so the rebuild re-isolates.
  /// </summary>
  private void InitializeAppearance()
  {
    if (!GodotObject.IsInstanceValid(_meshRoot))
    {
      _faces.Clear();
      _isolated = false;
    }

    if (!_isolated)
    {
      Node? meshRoot = MeshRoot.IsEmpty ? null : GetNodeOrNull(MeshRoot);
      if (meshRoot is null && !MeshRoot.IsEmpty)
        throw new InvalidOperationException(
          $"The character model '{Name}' mesh root path '{MeshRoot}' does not resolve.");
      if (meshRoot is not null)
      {
        _meshRoot = meshRoot;
        _faces.Clear();
        ModelMaterials.Isolate(meshRoot);
        CollectFaces(meshRoot);
        _isolated = true;
      }
    }

    // _EnterTree runs parent-first, before the face skeletons have entered:
    // it isolates but does not sample, because their global bases are
    // unreadable outside the tree (the engine rejects the read). Axes compute
    // at the root's own _Ready and every _Process instead.
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

  private void CollectFaces(Node meshRoot)
  {
    foreach (Node node in meshRoot.FindChildren("*", "MeshInstance3D", true, false))
    {
      var mesh = (MeshInstance3D)node;
      if (mesh.Mesh is null)
        continue;
      for (int surface = 0; surface < mesh.Mesh.GetSurfaceCount(); surface++)
      {
        if (mesh.GetSurfaceOverrideMaterial(surface) is not ShaderMaterial material
          || !material.GetShaderParameter(UseFaceSdfParameter).AsBool())
          continue;
        Skeleton3D? skeleton = mesh.GetNodeOrNull(mesh.Skeleton) as Skeleton3D;
        if (skeleton is null)
          throw new InvalidOperationException(
            $"The character model '{Name}' face material on '{mesh.Name}' has no skeleton.");
        int head = skeleton.FindBone(HeadBoneName);
        if (head < 0)
          throw new InvalidOperationException(
            $"The character model '{Name}' face skeleton '{skeleton.Name}' has no '{HeadBoneName}' bone.");
        _faces.Add(new FaceLighting
        {
          Material = material,
          Skeleton = skeleton,
          Head = head,
          InverseGlobalRest = skeleton.GetBoneGlobalRest(head).Basis.Inverse(),
        });
      }
    }
  }
}
