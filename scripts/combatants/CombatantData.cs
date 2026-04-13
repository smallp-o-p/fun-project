using FunProject.Core;
using FunProject.Stats;
using Godot;

namespace FunProject.Combatants;

[GlobalClass]
public partial class CombatantData : NamedEntityData
{
  [Export] public HealthStat HealthStat { get; set; }
  [Export] public ActionPointsStat ActionPointsStat { get; set; }
  [Export] public WillStat WillStat { get; set; }
  [Export] public MovementStat MovementStat { get; set; }
  [Export] public VisionStat VisionStat { get; set; } = new VisionStat { BaseValue = 20 };
  [Export] public AimStat AimStat { get; set; }
  [Export] public BaseArmorStat BaseArmorStat {get; set; }
  [Export] public int ModSlotCount { get; set; }
}
