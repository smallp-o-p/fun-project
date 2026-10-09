using Godot;
namespace FunProject.Core;

[GlobalClass]
public partial class NamedEntityData : Resource
{
  [Export] public string Name { get; set; } = "Name";

  [Export(PropertyHint.MultilineText)] public string Description { get; set; } = "Description";
}

public interface IHasNameAndDescription
{
  public string GetName();
  public string GetDescription();
};
