using Godot;

namespace FunProject.Models;

/// <summary>Shared authored mask contribution; its bound region remains per-instance.</summary>
[Tool, GlobalClass]
public partial class ModelWardrobeMaskRule : Resource
{
  [Export] public NodePath Path { get; set; } = new();
  [Export] public StringName Name { get; set; } = new();
  [Export] public int Index { get; set; }
  [Export] public string Component { get; set; } = "";
  [Export] public bool Enabled { get; set; } = true;
  [Export] public int Variant { get; set; } = -1;
}
