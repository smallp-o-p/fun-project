using Godot;

namespace FunProject.Items.Capabilities;

[GlobalClass]
public partial class ModSlotsCapabilityData : ItemCapabilityData
{
  [Export] public int SlotCount { get; set; }

  public override ItemCapability CreateRuntime() => new ModSlotsCapability(this);
}
