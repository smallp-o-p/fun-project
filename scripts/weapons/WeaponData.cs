using Godot;
using FunProject.Core;
using FunProject.Items;
using FunProject.Stats;

namespace FunProject.Weapons;

public enum DamageElement
{
  Kinetic,
  Thermal,
  Electrical,
  Chem,
}

[GlobalClass]
public partial class WeaponData : EquippableItemData
{
  [Export] public DamageElement DamageElement { get; set; }
  [Export] required public DamageStat DamageStat { get; set; }
  [Export] required public RangeStat RangeStat { get; set; }
  [Export] required public CriticalChanceStat CriticalChanceStat { get; set; }
}
