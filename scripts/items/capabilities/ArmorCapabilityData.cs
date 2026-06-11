using FunProject.Stats;
using Godot;

namespace FunProject.Items.Capabilities;

[GlobalClass]
public partial class ArmorCapabilityData : ItemCapabilityData
{
  [Export] required public BaseArmorStat ArmorStat { get; set; }
  [Export] public int RegenDelayTurns { get; set; }
  [Export] public int RegenPerTurn { get; set; }

  public override ItemCapability CreateRuntime() => new ArmorCapability(this);
}
