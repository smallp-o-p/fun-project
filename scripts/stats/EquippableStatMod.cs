using Godot;

namespace FunProject.Stats;

[GlobalClass]
public partial class EquippableStatMod : StatMod
{
  [Export] public string Name { get; set; } = "";
  [Export] public string Description { get; set; } = "";
}
