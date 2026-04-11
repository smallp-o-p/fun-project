using System;
using System.Collections.Generic;
using FunProject.Items;
using FunProject.Stats;

namespace FunProject.Weapons;

public class Weapon : EquippableItem, HasStats
{
  public string WeaponName => ItemName;
  public DamageElement DamageElement { get; }
  protected readonly Dictionary<Type, Stat> _stats;

  public Weapon(WeaponData data) : base(data)
  {
    DamageElement = data.DamageElement;

    _stats = new Dictionary<Type, Stat>
    {
      [typeof(DamageStat)] = data.DamageStat,
      [typeof(RangeStat)] = data.RangeStat,
      [typeof(CriticalChanceStat)] = data.CriticalChanceStat,
    };
  }

  public int NumModslots() => ModSlots.Count;

  public bool TryGetStat(Type statType, out Stat stat) => _stats.TryGetValue(statType, out stat);

  public bool TryGetStat<TStat>(out TStat stat) where TStat : Stat
  {
    if (_stats.TryGetValue(typeof(TStat), out var foundStat))
    {
      stat = (TStat)foundStat;
      return true;
    }

    stat = null!;
    return false;
  }

  public TStat GetStat<TStat>() where TStat : Stat
  {
    if (TryGetStat<TStat>(out var stat))
      return stat;

    throw new InvalidOperationException();
  }

  public DamageStat GetDamageStat() => GetStat<DamageStat>();
  public RangeStat GetRangeStat() => GetStat<RangeStat>();
  public CriticalChanceStat GetCritChanceStat() => GetStat<CriticalChanceStat>();
}

public class MeleeWeapon(WeaponData data) : Weapon(data)
{
}

public class AmmunitionedWeapon : Weapon
{
  public AmmunitionedWeapon(AmmunitionedWeaponData data) : base(data)
  {
    _stats[typeof(AmmunitionStat)] = data.AmmunitionStat;
  }

  public AmmunitionStat GetMagAmmoStat() => GetStat<AmmunitionStat>();
}

public class FirearmWeapon(FirearmWeaponData data) : AmmunitionedWeapon(data)
{
  public FirearmArchetype Archetype { get; set; } = data.Archetype;
  public Ammunition AmmoType { get; set; } = new Ammunition(data.DefaultAmmoData);
}
