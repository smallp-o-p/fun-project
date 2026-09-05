using FunProject.Buffs;
using Godot;

namespace FunProject.Items.Capabilities;

[GlobalClass]
public partial class BuffGrantCapabilityData : ItemCapabilityData
{
  [Export] public Godot.Collections.Array<Buff> Buffs { get; set; } = [];

  public override ItemCapability CreateRuntime() => new BuffGrantCapability(this);
}
