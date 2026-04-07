using FunProject.Core;
using FunProject.Stats;
using Godot;

namespace FunProject.Combatants;

[GlobalClass]
public partial class CombatantData : NamedEntityData
{
  [Export] public Stat HealthStat { get; set; }
  [Export] public Stat ActionPointsStat { get; set; }
  [Export] public Stat WillStat { get; set; }
  [Export] public Stat MovementStat { get; set; }
  [Export] public Stat AimStat { get; set; }
  [Export] public Stat BaseArmorStat {get; set; }
  [Export] public int ModSlotCount { get; set; }
}
