using Godot;
namespace FunProject.Core;

[GlobalClass]
public partial class NamedEntityData : Resource
{
  [Export] public string Name { get; set; }
  [Export] public string Description { get; set; }
}
