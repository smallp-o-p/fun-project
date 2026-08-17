using System;
using System.Collections.Generic;
using Godot;
using FunProject.Items;
using FunProject.Stats;

namespace FunProject.Weapons;

public class Weapon : EquippableItem, HasStats
{
  protected readonly StatSheet _stats;
  protected readonly WeaponFrameData _frame;

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

  public Option<TStat> TryGetStat<TStat>() where TStat : Stat => _stats.TryGetStat<TStat>();

  public TStat GetStat<TStat>() where TStat : Stat => _stats.GetStat<TStat>();

  /// <summary>Emit this weapon's damage bundle with no external contributions.</summary>
  public List<Damage> EmitDamage() => EmitDamage([]);

  /// <summary>
  /// Derive the frame's packets from base damage, fold this weapon's
  /// <see cref="DamageContributions"/> followed by <paramref name="externalMods"/> (e.g. the
  /// wielder's active buff mods), and drop non-positive packets at emission.
  /// </summary>
  public List<Damage> EmitDamage(IEnumerable<DamageBundleMod> externalMods)
  {
    int baseDamage = GetDamageStat().BaseValue;
    var context = new DamageEmissionContext(baseDamage);

    List<Damage> bundle = _frame.Packets.AsValueEnumerable()
      .Select(packet => packet.Derive(baseDamage))
      .ToList();

    foreach (DamageBundleMod mod in DamageContributions.AsValueEnumerable().Concat(externalMods))
    {
      ArgumentNullException.ThrowIfNull(mod);
      bundle = mod.Apply(bundle, context);
    }

    return bundle.AsValueEnumerable().Where(damage => damage.Amount > 0).ToList();
  }

  public Option<List<Damage>> TrySpendShot() => TrySpendShot([]);

  /// <summary>
  /// Spend one shot and emit its damage bundle (external mods fold last), or None if the
  /// weapon cannot fire. Weapons without a magazine never deplete; <see cref="AmmunitionedWeapon"/>
  /// overrides this with ammunition gating.
  /// </summary>
  public virtual Option<List<Damage>> TrySpendShot(IEnumerable<DamageBundleMod> externalMods)
    => EmitDamage(externalMods);

  /// <summary>Whether this weapon can fire right now. Weapons without a magazine are always loaded.</summary>
  public virtual bool IsLoaded => true;

  /// <summary>Whether this weapon carries a reloadable magazine.</summary>
  public virtual bool HasMagazine => false;

  /// <summary>Whether reloading would change anything. Weapons without a magazine never reload.</summary>
  public virtual bool CanReload() => false;

  public float EffectiveStat<TStat>() where TStat : Stat
    => this.Resolve<TStat>(StatContributions);

  public int EffectiveRange => Mathf.RoundToInt(EffectiveStat<RangeStat>());

  /// <summary>StatMods this weapon contributes to resolution; override to add more (e.g. ammunition).</summary>
  public virtual IEnumerable<StatMod> StatContributions
    => this.EquippedMods().AsValueEnumerable().SelectMany(m => m.StatContributions).ToArray();

  /// <summary>
  /// DamageBundleMods this weapon contributes to every emission, folded before any external
  /// mods. Base: slot-mounted mods; override to append more (e.g. ammunition), mirroring
  /// <see cref="StatContributions"/>.
  /// </summary>
  protected virtual IEnumerable<DamageBundleMod> DamageContributions
    => this.EquippedMods().AsValueEnumerable().OfType<DamageBundleEquippableMod>().SelectMany(m => m.BundleMods).ToArray();

  public DamageStat GetDamageStat() => GetStat<DamageStat>();
  public RangeStat GetRangeStat() => GetStat<RangeStat>();
  public CriticalChanceStat GetCritChanceStat() => GetStat<CriticalChanceStat>();
}

public class MeleeWeapon(WeaponData data) : Weapon(data)
{
}

public class AmmunitionedWeapon : Weapon
{
  // Runtime magazine state. Plain int on the runtime wrapper — never a Stat resource,
  // so battle-time spending can never write through to the authored template.
  public int CurrentAmmo { get; private set; }

  public AmmunitionedWeapon(AmmunitionedWeaponData data) : base(data)
  {
    _stats.Set(data.AmmunitionStat);
    CurrentAmmo = MagazineSize;
  }

  public int MagazineSize => Mathf.RoundToInt(EffectiveStat<AmmunitionStat>());

  // One shot spends one round per frame packet.
  public int ShotCost => _frame.Packets.Count;

  public override bool IsLoaded => !NeedsToReload();

  public override bool HasMagazine => true;

  public override bool CanReload() => CurrentAmmo < MagazineSize;

  public bool NeedsToReload() => CurrentAmmo < ShotCost;

  public void Reload() => CurrentAmmo = MagazineSize;

  public override Option<List<Damage>> TrySpendShot(IEnumerable<DamageBundleMod> externalMods)
  {
    if (NeedsToReload())
      return None;

    CurrentAmmo -= ShotCost;
    return EmitDamage(externalMods);
  }
}

public class FirearmWeapon(FirearmWeaponData data) : AmmunitionedWeapon(data)
{
  public FirearmArchetype Archetype { get; set; } = data.Archetype;
  public Option<Ammunition> AmmoType { get; set; } = Optional(data.DefaultAmmoData);

  protected override IEnumerable<DamageBundleMod> DamageContributions
    => base.DamageContributions.AsValueEnumerable().Concat(AmmunitionBundleMods()).ToArray();

  public override IEnumerable<StatMod> StatContributions => base.StatContributions.AsValueEnumerable().Concat(AmmunitionStatMods()).ToArray();

  private IEnumerable<DamageBundleMod> AmmunitionBundleMods()
    => AmmoType.Match(ammo => (IEnumerable<DamageBundleMod>)ammo.DamageMods, () => []);

  private IEnumerable<StatMod> AmmunitionStatMods()
    => AmmoType.Match(ammo => (IEnumerable<StatMod>)ammo.Modifiers, () => []);
}
