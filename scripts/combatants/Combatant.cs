using System;
using System.Collections.Generic;
using FunProject.Buffs;
using FunProject.Items;
using FunProject.Stats;

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
  public IReadOnlyList<BuffData> InnateBuffs { get; }

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
    InnateBuffs = [.. data.InnateBuffs];

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
    => this.EquippedMods().AsValueEnumerable().SelectMany(m => m.StatContributions).Concat(OwningFaction.StatBonuses).ToArray();

  public void EquipItem(EquippableItem item, int slot)
  {
    ArgumentNullException.ThrowIfNull(item);
    if (slot < 0 || slot >= MaxInventorySize)
      throw new InvalidOperationException(
        $"EquipItem slot {slot} is out of range for an inventory of size {MaxInventorySize}.");

    _inventory[slot] = item;
  }
}
