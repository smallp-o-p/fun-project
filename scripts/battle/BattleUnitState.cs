using System;
using FunProject.Combatants;
using FunProject.Items;
using FunProject.Stats;
using FunProject.Weapons;
using System.Collections.Generic;

namespace FunProject.Battle;

public sealed class BattleUnitState
{
  private readonly List<EquippableItem> _inventory = [];
  private readonly SysColGeneric.HashSet<BattleUnitState> _visibleUnits = [];
  private readonly SysColGeneric.HashSet<BattleBoardState.ValidatedPoint> _visibleTiles = [];

  internal int Id { get; }
  public Combatant Combatant { get; }
  public Faction Side => Combatant.OwningFaction;
  public Option<Weapon> EquippedWeapon { get; private set; }
  public IReadOnlyList<EquippableItem> Inventory => _inventory;
  internal IReadOnlySet<BattleUnitState> VisibleUnits => _visibleUnits;
  internal IReadOnlySet<BattleBoardState.ValidatedPoint> VisibleTiles => _visibleTiles;

  public int MaxHealth => GetBaseStatValue<HealthStat>();
  public int CurrentHealth { get; private set; }
  public int MaxActionPoints => GetBaseStatValue<ActionPointsStat>();
  public int CurrentActionPoints { get; private set; }
  public int Movement => GetBaseStatValue<MovementStat>();
  public int Vision => GetBaseStatValue<VisionStat>();
  public bool IsAlive => CurrentHealth > 0;
  public bool IsDead => !IsAlive;

  internal static BattleUnitState Create(
    int unitId,
    Combatant combatant,
    Option<Weapon> equippedWeapon)
  {
    return new BattleUnitState(unitId, combatant, equippedWeapon);
  }

  private BattleUnitState(int unitId, Combatant combatant, Option<Weapon> equippedWeapon)
  {
    ArgumentOutOfRangeException.ThrowIfLessThan(unitId, 0);

    Id = unitId;
    ArgumentNullException.ThrowIfNull(combatant);
    Combatant = combatant;
    EquippedWeapon = equippedWeapon;
    CurrentHealth = MaxHealth;
    CurrentActionPoints = MaxActionPoints;
  }

  public void RefreshForNewTurn()
  {
    CurrentActionPoints = MaxActionPoints;
  }

  public bool TrySpendActionPoints(int cost)
  {
    if (cost < 0 || CurrentActionPoints < cost)
      return false;

    CurrentActionPoints -= cost;
    return true;
  }

  public void ReceiveDamage(int amount)
  {
    if (amount <= 0)
      return;

    CurrentHealth = Math.Max(CurrentHealth - amount, 0);
  }

  public void EquipWeapon(Weapon weapon)
  {
    EquippedWeapon = Some(weapon);
  }

  public void AddInventoryItem(EquippableItem item)
  {
    _inventory.Add(item);
  }

  public bool HasInventoryItem(EquippableItem item)
  {
    return _inventory.Contains(item);
  }

  public bool RemoveInventoryItem(EquippableItem item)
  {
    return _inventory.Remove(item);
  }

  internal void ClearVisibility()
  {
    _visibleUnits.Clear();
    _visibleTiles.Clear();
  }

  internal void AddVisibleTile(BattleBoardState.ValidatedPoint tile)
  {
    _visibleTiles.Add(tile);
  }

  internal void AddVisibleUnit(BattleUnitState unit)
  {
    ArgumentNullException.ThrowIfNull(unit);
    _visibleUnits.Add(unit);
  }

  private int GetBaseStatValue<TStat>() where TStat : Stat
  {
    return Combatant.TryGetStat<TStat>().Match(
      stat => stat.BaseValue,
      () => 0);
  }
}
