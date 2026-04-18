using System;
using Godot;

#nullable enable
public partial class TestInteractObject : Interactable
{
  private MeshInstance3D? clickableMesh;

  public override void _Ready()
  {
    base._Ready();

    clickableMesh = FindMeshInstance(this);
    if (clickableMesh == null)
    {
      GD.PushError($"{nameof(TestInteractObject)} '{Name}' needs a MeshInstance3D child to change color on click.");
    }
  }

  public override bool CanInteract(Camera3D camera, Vector3 eventPosition, Vector3 normal, int shapeIdx)
  {
    ArgumentNullException.ThrowIfNull(camera);

    return ResolveClickableMesh() != null;
  }

  public override void OnInteracted(Camera3D camera, Vector3 eventPosition, Vector3 normal, int shapeIdx)
  {
    ArgumentNullException.ThrowIfNull(camera);

    MeshInstance3D? meshInstance = ResolveClickableMesh();
    if (meshInstance == null)
    {
      GD.PushError($"{nameof(TestInteractObject)} '{Name}' could not find a MeshInstance3D to recolor.");
      return;
    }

    meshInstance.MaterialOverride = CreateClickedMaterial();
  }

  public override void OnHoverStarted()
  {
    ResolveClickableMesh()?.MaterialOverride = new StandardMaterial3D
    {
      AlbedoColor = Colors.Pink
    };
  }

  public override void OnHoverEnded()
  {
    ResolveClickableMesh()?.MaterialOverride = new StandardMaterial3D
    {
      AlbedoColor = Colors.Red
    };

  }

  private MeshInstance3D? ResolveClickableMesh()
  {
    clickableMesh ??= FindMeshInstance(this);
    return clickableMesh;
  }

  private static StandardMaterial3D CreateClickedMaterial()
  {
    return new StandardMaterial3D
    {
      AlbedoColor = Colors.Blue
    };
  }

  private static MeshInstance3D? FindMeshInstance(Node node)
  {
    ArgumentNullException.ThrowIfNull(node);

    foreach (Node child in node.GetChildren())
    {
      if (child is MeshInstance3D meshInstance)
      {
        return meshInstance;
      }

      MeshInstance3D? nestedMeshInstance = FindMeshInstance(child);
      if (nestedMeshInstance != null)
      {
        return nestedMeshInstance;
      }
    }

    return null;
  }
}
