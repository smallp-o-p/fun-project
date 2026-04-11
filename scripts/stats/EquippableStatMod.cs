using Godot;

namespace FunProject.Stats;

public abstract partial class EquippableStatMod : StatMod
{
  [Export] public string Name { get; set; } = "";
  [Export] public string Description { get; set; } = "";
}
