using FunProject.Items.Effects;
using Godot;
using Godot.Collections;

namespace FunProject.Items.Capabilities;

[GlobalClass]
public partial class BlastCapabilityData : ItemCapabilityData
{
  [Export] public int BlastRadius { get; set; } = 1;
  [Export] public Array<BattleEffectData> Effects { get; set; } = [];

  public override ItemCapability CreateRuntime() => new BlastCapability(this);
}
