using Godot;

namespace FunProject.Stats;

public enum StatType
{
  Health,
  Damage,
  Range,
  Ammunition,
  CriticalChance,
  ActionPoints,
  Will,
  Movement,
  Aim,
  BaseArmor
}

[GlobalClass]
public partial class Stat : Resource
{
  [Export] public StatType StatType { get; set; }
  [Export] public int BaseValue { get; set; } = 0;
}

public interface HasStats
{
  Godot.Collections.Dictionary<StatType, Stat> GetStats();
}
