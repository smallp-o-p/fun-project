using Godot;
using FunProject.Items;
using FunProject.Stats;

namespace FunProject.Weapons;

[GlobalClass]
public partial class WeaponData : EquippableItemData
{
  [Export] required public WeaponFrameData Frame { get; set; }
  [Export] required public DamageStat DamageStat { get; set; }
  [Export] required public RangeStat RangeStat { get; set; }
  [Export] required public CriticalChanceStat CriticalChanceStat { get; set; }
}
