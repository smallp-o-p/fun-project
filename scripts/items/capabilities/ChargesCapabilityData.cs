using Godot;

namespace FunProject.Items.Capabilities;

[GlobalClass]
public partial class ChargesCapabilityData : ItemCapabilityData
{
  [Export] public int MaxCharges { get; set; } = 1;

  public override ItemCapability CreateRuntime() => new ChargesCapability(this);
}
