using System;
using System.Collections.Generic;
using System.Linq;
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

  /// <summary>
  /// Emit this weapon's damage bundle. Override to give a weapon type its own emission;
  /// the common case (contributing extra bundle mods) should delegate to <see cref="EmitDamageWith"/>.
  /// </summary>
  public virtual List<Damage> EmitDamage() => EmitDamageWith([]);

  /// <summary>
  /// Spend one shot and emit its damage bundle, or None if the weapon cannot fire.
  /// Weapons without a magazine never deplete; <see cref="AmmunitionedWeapon"/> overrides
  /// this with ammunition gating.
  /// </summary>
  public virtual Option<List<Damage>> TrySpendShot() => EmitDamage();

  /// <summary>Whether this weapon can fire right now. Weapons without a magazine are always loaded.</summary>
  public virtual bool IsLoaded => true;

  /// <summary>Whether this weapon carries a reloadable magazine.</summary>
  public virtual bool HasMagazine => false;

  /// <summary>Whether reloading would change anything. Weapons without a magazine never reload.</summary>
  public virtual bool CanReload() => false;

  /// <summary>
  /// Derive the frame's packets from base damage, fold the weapon's slot bundle mods followed by
  /// <paramref name="additionalMods"/>, and drop non-positive packets at emission.
  /// </summary>
  protected List<Damage> EmitDamageWith(IEnumerable<DamageBundleMod> additionalMods)
  {
    int baseDamage = GetDamageStat().BaseValue;
    var context = new DamageEmissionContext(baseDamage);

    List<Damage> bundle = _frame.Packets
      .Select(packet => packet.Derive(baseDamage))
      .ToList();

    foreach (DamageBundleMod mod in BundleModsFromSlots().Concat(additionalMods))
    {
      ArgumentNullException.ThrowIfNull(mod);
      bundle = mod.Apply(bundle, context);
    }

    return bundle.Where(damage => damage.Amount > 0).ToList();
  }

  public float EffectiveStat<TStat>() where TStat : Stat
    => ((HasStats)this).Resolve<TStat>(StatContributions);

  public int EffectiveRange => Mathf.RoundToInt(EffectiveStat<RangeStat>());

  /// <summary>StatMods this weapon contributes to resolution; override to add more (e.g. ammunition).</summary>
  public virtual IEnumerable<StatMod> StatContributions
    => this.EquippedMods().SelectMany(m => m.StatContributions);

  private IEnumerable<DamageBundleMod> BundleModsFromSlots()
    => this.EquippedMods().OfType<DamageBundleEquippableMod>().SelectMany(m => m.BundleMods);

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

  public override Option<List<Damage>> TrySpendShot()
  {
    if (NeedsToReload())
      return None;

    CurrentAmmo -= ShotCost;
    return EmitDamage();
  }
}

public class FirearmWeapon(FirearmWeaponData data) : AmmunitionedWeapon(data)
{
  public FirearmArchetype Archetype { get; set; } = data.Archetype;
  public Option<Ammunition> AmmoType { get; set; } = Optional(data.DefaultAmmoData);

  public override List<Damage> EmitDamage() => EmitDamageWith(AmmunitionBundleMods());

  public override IEnumerable<StatMod> StatContributions => base.StatContributions.Concat(AmmunitionStatMods());

  private IEnumerable<DamageBundleMod> AmmunitionBundleMods()
    => AmmoType.Match(ammo => (IEnumerable<DamageBundleMod>)ammo.DamageMods, () => []);

  private IEnumerable<StatMod> AmmunitionStatMods()
    => AmmoType.Match(ammo => (IEnumerable<StatMod>)ammo.Modifiers, () => []);
}
