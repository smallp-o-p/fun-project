using FunProject.Buffs;
using Godot;

namespace FunProject.Progression;

/// <summary>Grants buffs that join the unit's spawn-time buff set (deduped by resource).</summary>
[GlobalClass]
public partial class BuffGrantUpgradeEffectData : UpgradeEffectData
{
  [Export] public Godot.Collections.Array<BuffData> Buffs { get; set; } = [];
}
