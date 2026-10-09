using Godot;

namespace FunProject.Items.Capabilities;

[GlobalClass]
public partial class UtilityCapabilityData : ItemCapabilityData
{
  public override ItemCapability CreateRuntime() => new UtilityCapability();
}
