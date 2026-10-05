using Godot;

namespace FunProject.Models;

/// <summary>
/// Import-generated per-mesh mask binding: the model-root-relative path of the masked
/// <c>MeshInstance3D</c> and the typed mask configuration describing its
/// geometry. Compiled from checked source identities; read once at initialization.
/// </summary>
[Tool, GlobalClass]
public partial class ModelMeshMaskSetup : Resource
{
  [Export] public NodePath MeshPath { get; set; } = new();

  [Export] public ModelMaskConfiguration? Configuration { get; set; }
}
