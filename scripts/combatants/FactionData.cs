using FunProject.Combatants;
using FunProject.Core;
using FunProject.Stats;
using Godot;
using Godot.Collections;

[GlobalClass]
public partial class FactionData : NamedEntityData
{
  [Export] public Array<StatMod> FactionBonuses { get; set; } = [];
  [Export] public Array<CombatantData> FactionCombatantTypes { get; set; } = [];
  [Export] public Dictionary<FactionData, Stat> BaseFriendlinessToOthers { get; set; }= [];
}
