using FunProject.Core;
using FunProject.Stats;
using Godot;
using Godot.Collections;

namespace FunProject.Weapons;

public enum DamageElement
{
  Kinetic,
  Thermal,
  Electrical,
  Chem,
}

public enum FirearmArchetype
{
  Pistol,
  SniperRifle,
  AssaultRifle,
  Shotgun,
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

[GlobalClass]
public partial class AmmunitionedWeaponData : WeaponData
{
  [Export] public Stat AmmunitionStat { get; set; }
}

[GlobalClass]
public partial class FirearmWeaponData : AmmunitionedWeaponData
{
  [Export] public FirearmArchetype Archetype { get; set; }
}

public class Weapon : HasStats, HasModSlots
{
  public string WeaponName { get; }
  public DamageElement DamageElement { get; }
  protected Array<ModSlot> ModSlots { get; } = [];
  protected readonly Dictionary<StatType, Stat> _stats;

  public Weapon(WeaponData data)
  {
    WeaponName = data.Name;
    DamageElement = data.DamageElement;

    _stats = new Dictionary<StatType, Stat>
    {
      [StatType.Damage] = data.DamageStat,
      [StatType.Range] = data.RangeStat,
      [StatType.CriticalChance] = data.CriticalChanceStat,
    };

    for(int i = 0; i < data.ModSlotCount; i++)
    {
      ModSlots.Add(new ModSlot());
    }
  }

  public int NumModslots() => ModSlots.Count;

  public Dictionary<StatType, Stat> GetStats() => _stats;
  public Array<ModSlot> GetModSlots() => ModSlots;
  public Stat GetDamageStat() => _stats[StatType.Damage];
  public Stat GetRangeStat() => _stats[StatType.Range];
  public Stat GetCritChanceStat() => _stats[StatType.CriticalChance];
}

public class MeleeWeapon : Weapon
{
  public MeleeWeapon(WeaponData data) : base(data) { }
}

public class AmmunitionedWeapon : Weapon
{
  public AmmunitionedWeapon(AmmunitionedWeaponData data) : base(data)
  {
    _stats[StatType.Ammunition] = data.AmmunitionStat;
  }

  public Stat GetMagAmmoStat() => _stats[StatType.Ammunition];
}

public class FirearmWeapon : AmmunitionedWeapon
{
  public FirearmArchetype Archetype { get; set; }
  public FirearmWeapon(FirearmWeaponData data) : base(data)
  {
    Archetype = data.Archetype;
  }
}
