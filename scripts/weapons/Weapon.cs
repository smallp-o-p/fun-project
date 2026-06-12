using System;
using System.Collections.Generic;
using System.Linq;
using FunProject.Items;
using FunProject.Stats;

namespace FunProject.Weapons;

public class Weapon : EquippableItem, HasStats
{
  protected readonly StatSheet _stats;
  private readonly WeaponFrameData _frame;

  public Weapon(WeaponData data) : base(data)
  {
    _frame = data.Frame ?? throw new InvalidOperationException($"Weapon '{ItemName}' has no frame.");
    if (_frame.Packets.Count == 0)
      throw new InvalidOperationException($"Weapon '{ItemName}' frame '{_frame.Name}' has no damage packets.");
    foreach (DamagePacketData packet in _frame.Packets)
      ArgumentNullException.ThrowIfNull(packet);

    _stats = new StatSheet(new Dictionary<Type, Stat>
    {
      [typeof(DamageStat)] = data.DamageStat,
      [typeof(RangeStat)] = data.RangeStat,
      [typeof(CriticalChanceStat)] = data.CriticalChanceStat,
    });
  }

  public Option<Stat> TryGetStat(Type statType) => _stats.TryGetStat(statType);

  public Option<TStat> TryGetStat<TStat>() where TStat : Stat => _stats.TryGetStat<TStat>();

  public TStat GetStat<TStat>() where TStat : Stat => _stats.GetStat<TStat>();

  public List<Damage> EmitDamage()
  {
    int baseDamage = GetDamageStat().BaseValue;
    var context = new DamageEmissionContext(baseDamage);

    List<Damage> bundle = _frame.Packets
      .Select(packet => packet.Derive(baseDamage))
      .ToList();

    foreach (DamageBundleMod mod in BundleModsFromSlots().Concat(AmmunitionBundleMods()))
    {
      ArgumentNullException.ThrowIfNull(mod);
      bundle = mod.Apply(bundle, context);
    }

    return bundle.Where(damage => damage.Amount > 0).ToList();
  }

  protected virtual IEnumerable<DamageBundleMod> AmmunitionBundleMods() => [];

  private IEnumerable<DamageBundleMod> BundleModsFromSlots()
  {
    foreach (ModSlot slot in GetModSlots())
    {
      if (slot.EquippedMod.IsNone)
        continue;
      EquippableMod equipped = slot.EquippedMod.Match(
        mod => mod,
        () => throw new InvalidOperationException("EquippedMod option was None after IsNone check."));
      if (equipped is DamageBundleEquippableMod bundleEquippable)
        foreach (DamageBundleMod mod in bundleEquippable.BundleMods)
          yield return mod;
    }
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
    _stats.Set(data.AmmunitionStat);
  }

  public AmmunitionStat GetMagAmmoStat() => GetStat<AmmunitionStat>();
}

public class FirearmWeapon(FirearmWeaponData data) : AmmunitionedWeapon(data)
{
  public FirearmArchetype Archetype { get; set; } = data.Archetype;
  public Option<Ammunition> AmmoType { get; set; } = Optional(data.DefaultAmmoData);

  protected override IEnumerable<DamageBundleMod> AmmunitionBundleMods()
    => AmmoType.Match(ammo => (IEnumerable<DamageBundleMod>)ammo.DamageMods, () => []);
}
