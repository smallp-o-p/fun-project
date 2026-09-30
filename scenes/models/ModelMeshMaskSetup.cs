using Godot;

namespace FunProject.Models;

/// <summary>
/// Authored per-mesh mask setup: the model-root-relative path of the masked
/// <c>MeshInstance3D</c> and the typed mask configuration describing its
/// geometry. Read once at initialization; in-place edits afterwards are not
/// picked up.
/// </summary>
[Tool, GlobalClass]
public partial class ModelMeshMaskSetup : Resource
{
  [Export] public NodePath MeshPath { get; set; } = new();

  [Export] public ModelMaskConfiguration? Configuration { get; set; }
}
