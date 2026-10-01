using Godot;

namespace FunProject.Models;

/// <summary>One authored clothing toggle and its outfit-specific garment nodes.</summary>
[Tool, GlobalClass]
public partial class ModelWardrobeComponent : Resource
{
  [Export] public bool Visible { get; set; } = true;
  [Export] public Godot.Collections.Array<ModelWardrobeGarment> Pieces { get; set; } = [];
}
