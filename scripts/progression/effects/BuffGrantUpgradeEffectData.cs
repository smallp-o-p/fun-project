using FunProject.Buffs;
using Godot;

namespace FunProject.Progression;

/// <summary>Grants buffs that join the unit's spawn-time grants; identical buffs stack.</summary>
[GlobalClass]
public partial class BuffGrantUpgradeEffectData : UpgradeEffectData
{
  [Export] public Godot.Collections.Array<Buff> Buffs { get; set; } = [];
}
