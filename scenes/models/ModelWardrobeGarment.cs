using Godot;

namespace FunProject.Models;

/// <summary>A fixed model-relative garment path; variant -1 belongs to every outfit.</summary>
[Tool, GlobalClass]
public partial class ModelWardrobeGarment : Resource
{
  [Export] public NodePath Path { get; set; } = new();
  [Export] public int Variant { get; set; } = -1;
}
