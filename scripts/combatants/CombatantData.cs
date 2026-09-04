using FunProject.Buffs;
using FunProject.Core;
using FunProject.Progression;
using FunProject.Stats;
using Godot;

namespace FunProject.Combatants;

[GlobalClass]
public partial class CombatantData : NamedEntityData
{
  [Export] required public HealthStat HealthStat { get; set; }
  [Export] required public ActionPointsStat ActionPointsStat { get; set; }
  [Export] required public WillStat WillStat { get; set; }
  [Export] required public MovementStat MovementStat { get; set; }
  [Export] required public VisionStat VisionStat { get; set; } = new VisionStat { BaseValue = 20 };
  [Export] required public AimStat AimStat { get; set; }
  [Export] public int ModSlotCount { get; set; }
  [Export] public Godot.Collections.Array<BuffData> InnateBuffs { get; set; } = [];
  [Export] public RankTableData? RankTable { get; set; }
  [Export] public int InventorySize { get; set; } = 5;
}
