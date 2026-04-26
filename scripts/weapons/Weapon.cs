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

  public Option<Stat> TryGetStat(Type statType)
  {
    return _stats.TryGetValue(statType, out var stat)
      ? Some(stat)
      : None;
  }

  public Option<TStat> TryGetStat<TStat>() where TStat : Stat
  {
    if (_stats.TryGetValue(typeof(TStat), out var foundStat))
    {
      return Some((TStat)foundStat);
    }

    return None;
  }

  public TStat GetStat<TStat>() where TStat : Stat
  {
    return TryGetStat<TStat>().Match(
      stat => stat,
      () => throw new InvalidOperationException());
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
