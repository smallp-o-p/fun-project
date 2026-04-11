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

public abstract partial class Stat : Resource
{
  public abstract StatType StatType { get; }
  [Export] public int BaseValue { get; set; } = 0;
}

public interface HasStats
{
  Godot.Collections.Dictionary<StatType, Stat> GetStats();
}
