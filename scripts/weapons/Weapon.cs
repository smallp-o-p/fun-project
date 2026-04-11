using FunProject.Items;
using FunProject.Stats;
using Godot.Collections;

namespace FunProject.Weapons;

public class Weapon : EquippableItem, HasStats
{
  public string WeaponName => ItemName;
  public DamageElement DamageElement { get; }
  protected readonly Dictionary<StatType, Stat> _stats;

  public Weapon(WeaponData data) : base(data)
  {
    DamageElement = data.DamageElement;

    _stats = new Dictionary<StatType, Stat>
    {
      [StatType.Damage] = data.DamageStat,
      [StatType.Range] = data.RangeStat,
      [StatType.CriticalChance] = data.CriticalChanceStat,
    };
  }

  public int NumModslots() => ModSlots.Count;

  public Dictionary<StatType, Stat> GetStats() => _stats;
  public DamageStat GetDamageStat() => (DamageStat)_stats[StatType.Damage];
  public RangeStat GetRangeStat() => (RangeStat)_stats[StatType.Range];
  public CriticalChanceStat GetCritChanceStat() => (CriticalChanceStat)_stats[StatType.CriticalChance];
}

public class MeleeWeapon(WeaponData data) : Weapon(data)
{
}

public class AmmunitionedWeapon : Weapon
{
  public AmmunitionedWeapon(AmmunitionedWeaponData data) : base(data)
  {
    _stats[StatType.Ammunition] = data.AmmunitionStat;
  }

  public AmmunitionStat GetMagAmmoStat() => (AmmunitionStat)_stats[StatType.Ammunition];
}

public class FirearmWeapon(FirearmWeaponData data) : AmmunitionedWeapon(data)
{
  public FirearmArchetype Archetype { get; set; } = data.Archetype;
  public Ammunition AmmoType { get; set; } = new Ammunition(data.DefaultAmmoData);
}
