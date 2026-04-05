using Godot;
using FunProject.Core;
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
public partial class WeaponData : NamedEntityData
{
  [Export] public DamageElement DamageElement { get; set; }
  [Export] public Stat DamageStat { get; set; }
  [Export] public Stat RangeStat { get; set; }
  [Export] public Stat CriticalChanceStat { get; set; }
  [Export] public int ModSlotCount { get; set; }
}
