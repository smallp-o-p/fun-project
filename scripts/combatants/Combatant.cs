using System;
using System.Collections.Generic;
using FunProject.Buffs;
using FunProject.Items;
using FunProject.Items.Capabilities;
using FunProject.Progression;
using FunProject.Stats;
using FunProject.Weapons;

namespace FunProject.Combatants;

/// <summary>
/// Saved state for a unit.
/// </summary>
public class Combatant : HasStats, HasModSlots
{
  public string Name { get; }
  public Faction OwningFaction { get; internal set; }
  private readonly StatSheet _stats;
  private readonly Godot.Collections.Array<ModSlot> _modSlots = [];
  private readonly Dictionary<int, EquippableItem> _inventory = [];
  public IReadOnlyDictionary<int, EquippableItem> Inventory => _inventory;
  public int MaxInventorySize { get; }

  /// <summary>Battle-facing weapon slot; mirrors BattleUnitState.EquippedWeapon.</summary>
  public Option<Weapon> EquippedWeapon { get; private set; }

  /// <summary>Battle-facing armor slot (capability proof); mirrors BattleUnitState.EquippedArmor.</summary>
  public Option<ItemWith<ArmorCapability>> EquippedArmor { get; private set; }

  private readonly List<Buff> _innateBuffs;

  /// <summary>Buffs innate to this soldier: authored at birth plus trained skill-path and
  /// rank grants, live-computed so an unlock or promotion lands on the next battle
  /// automatically. Identical buffs stack — overlaps between sources are deliberate, not deduped.</summary>
  public IReadOnlyList<Buff> InnateBuffs
    => _innateBuffs.AsValueEnumerable().Concat(Progression.GrantedBuffs()).Concat(Rank.GrantedBuffs()).ToArray();

  public UnitProgression Progression { get; } = new();

  /// <summary>Per-unit rank/XP state; ladder (names, factors, effects) from the combatant's
  /// authored table or the shared default. See <see cref="AwardBattleExperience"/>.</summary>
  public UnitRank Rank { get; }

  public Combatant(CombatantData data, Faction faction, Option<string> name = default)
  {
    ArgumentNullException.ThrowIfNull(data);
    ArgumentNullException.ThrowIfNull(faction);

    Name = name.IfNone(() => data.Name);
    OwningFaction = faction;
    _stats = new StatSheet(new Dictionary<Type, Stat>
    {
      [typeof(HealthStat)] = data.HealthStat,
      [typeof(ActionPointsStat)] = data.ActionPointsStat,
      [typeof(WillStat)] = data.WillStat,
      [typeof(MovementStat)] = data.MovementStat,
      [typeof(VisionStat)] = data.VisionStat,
      [typeof(AimStat)] = data.AimStat,
    });
    _innateBuffs = [.. data.InnateBuffs];
    Rank = new UnitRank(data.RankTable ?? DefaultRankTable.Table);

    for (int i = 0; i < data.ModSlotCount; i++)
    {
      _modSlots.Add(new ModSlot());
    }
    MaxInventorySize = data.InventorySize;
  }

  public Option<TStat> TryGetStat<TStat>() where TStat : Stat => _stats.TryGetStat<TStat>();

  public TStat GetStat<TStat>() where TStat : Stat => _stats.GetStat<TStat>();

  public Godot.Collections.Array<ModSlot> GetModSlots() => _modSlots;

  public IEnumerable<StatMod> StatContributions()
    => this.EquippedMods().AsValueEnumerable().SelectMany(m => m.StatContributions).Concat(OwningFaction.StatBonuses).Concat(Progression.StatMods()).Concat(Rank.StatMods()).ToArray();

  /// <summary>The single campaign-owned contribution policy: this combatant's own
  /// mods/faction/progression/rank contributions plus the currently equipped weapon's.
  /// Freshly materialized per call. Battle aggregates separately (BattleUnitState) because
  /// a battle may equip a different weapon and stacks active-buff mods on top.</summary>
  public IEnumerable<StatMod> CampaignStatContributions()
    => StatContributions().AsValueEnumerable()
      .Concat(EquippedWeapon.Match<SysColGeneric.IEnumerable<StatMod>>(
        weapon => weapon.StatContributions, []))
      .ToArray();

  public void EquipItem(EquippableItem item, int slot)
  {
    ArgumentNullException.ThrowIfNull(item);
    if (slot < 0 || slot >= MaxInventorySize)
      throw new InvalidOperationException(
        $"EquipItem slot {slot} is out of range for an inventory of size {MaxInventorySize}.");

    _inventory[slot] = item;
  }

  /// <summary>Equip the weapon slot; returns the displaced weapon (if any) for the caller to return to the armory.</summary>
  public Option<Weapon> EquipWeapon(Weapon weapon)
  {
    ArgumentNullException.ThrowIfNull(weapon);
    Option<Weapon> displaced = EquippedWeapon;
    EquippedWeapon = weapon;
    return displaced;
  }

  /// <summary>Clear the weapon slot; returns what was there for the caller to return to the armory.</summary>
  public Option<Weapon> UnequipWeapon()
  {
    Option<Weapon> removed = EquippedWeapon;
    EquippedWeapon = None;
    return removed;
  }

  /// <summary>Equip the armor slot from a capability proof; returns the displaced proof (if any).</summary>
  public Option<ItemWith<ArmorCapability>> EquipArmor(ItemWith<ArmorCapability> armor)
  {
    Option<ItemWith<ArmorCapability>> displaced = EquippedArmor;
    EquippedArmor = armor;
    return displaced;
  }

  /// <summary>Clear the armor slot; returns what was there for the caller to return to the armory.</summary>
  public Option<ItemWith<ArmorCapability>> UnequipArmor()
  {
    Option<ItemWith<ArmorCapability>> removed = EquippedArmor;
    EquippedArmor = None;
    return removed;
  }

  /// <summary>Clear one utility inventory slot; returns what was there for the caller to return to the armory.</summary>
  public Option<EquippableItem> UnequipItem(int slot)
  {
    if (slot < 0 || slot >= MaxInventorySize)
      throw new InvalidOperationException(
        $"UnequipItem slot {slot} is out of range for an inventory of size {MaxInventorySize}.");

    return _inventory.Remove(slot, out EquippableItem? item) ? item : None;
  }
}
