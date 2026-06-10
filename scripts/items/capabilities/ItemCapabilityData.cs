using Godot;

namespace FunProject.Items.Capabilities;

[GlobalClass]
public abstract partial class ItemCapabilityData : Resource
{
  public abstract ItemCapability CreateRuntime();
}
