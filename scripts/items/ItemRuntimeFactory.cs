using FunProject.Weapons;
using System;

namespace FunProject.Items;

/// <summary>
/// Central data -> runtime instantiation: the single place that knows a FirearmWeaponData
/// implies a FirearmWeapon runtime (etc.). The Armory and BattleFactory both route through
/// here so weapon wiring never forks.
/// </summary>
public static class ItemRuntimeFactory
{
  public static EquippableItem Create(EquippableItemData data)
  {
    ArgumentNullException.ThrowIfNull(data);
    return data switch
    {
      WeaponData weapon => CreateWeapon(weapon),
      _ => new EquippableItem(data),
    };
  }

  public static Weapon CreateWeapon(WeaponData data)
  {
    ArgumentNullException.ThrowIfNull(data);
    return data switch
    {
      FirearmWeaponData firearm => new FirearmWeapon(firearm),
      AmmunitionedWeaponData ammunitioned => new AmmunitionedWeapon(ammunitioned),
      _ => new Weapon(data),
    };
  }
}
