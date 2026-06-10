using Godot;

namespace FunProject.Items.Capabilities;

[GlobalClass]
public partial class ThrowableCapabilityData : ItemCapabilityData
{
  [Export] public int ThrowRange { get; set; } = 8;
  [Export] public int ActionPointCost { get; set; } = 1;
  [Export] public bool ConsumesOnUse { get; set; } = true;

  public override ItemCapability CreateRuntime() => new ThrowableCapability(this);
}
