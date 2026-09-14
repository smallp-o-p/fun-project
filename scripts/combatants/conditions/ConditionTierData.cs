using Godot;
using FunProject.Stats;

namespace FunProject.Combatants.Conditions;

[GlobalClass]
public partial class ConditionTierData : Resource
{
  [Export] public string Name { get; set; } = "";
  [Export] public uint RecoveryDays { get; set; }
  [Export] public Godot.Collections.Array<StatMod> StatMods { get; set; } = [];
}
