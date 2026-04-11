#nullable enable
using System;
using FunProject.Combatants;
using FunProject.Items;
using FunProject.Stats;
using FunProject.Weapons;
using Godot;
using System.Collections.Generic;

namespace FunProject.Battle;

public sealed class BattleUnitState
{
  private readonly List<EquippableItem> _inventory = [];

  public int UnitId { get; }
  public Combatant Combatant { get; }
  public Faction Side => Combatant.OwningFaction;
  public Vector3I Position { get; private set; }
  public Weapon? EquippedWeapon { get; private set; }
  public bool IsSelected { get; internal set; }
  public bool HasEndedActivationThisTurn { get; private set; }
  public IReadOnlyList<EquippableItem> Inventory => _inventory;

  public int MaxHealth => GetBaseStatValue<HealthStat>();
  public int CurrentHealth { get; private set; }
  public int MaxActionPoints => GetBaseStatValue<ActionPointsStat>();
  public int CurrentActionPoints { get; private set; }
  public int Movement => GetBaseStatValue<MovementStat>();
  public bool IsAlive => CurrentHealth > 0;
  public bool CanAct => IsAlive && CurrentActionPoints > 0 && !HasEndedActivationThisTurn;

  public BattleUnitState(int unitId, Combatant combatant, Vector3I position, Weapon? equippedWeapon = null)
  {
    UnitId = unitId;
    Combatant = combatant;
    Position = position;
    EquippedWeapon = equippedWeapon;
    CurrentHealth = MaxHealth;
    CurrentActionPoints = MaxActionPoints;
  }

  public void RefreshForNewTurn()
  {
    CurrentActionPoints = MaxActionPoints;
    HasEndedActivationThisTurn = false;
  }

  public void EndActivation()
  {
    HasEndedActivationThisTurn = true;
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

  public void MoveTo(Vector3I position)
  {
    Position = position;
  }

  public void EquipWeapon(Weapon weapon)
  {
    EquippedWeapon = weapon;
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

  private int GetBaseStatValue<TStat>() where TStat : Stat
  {
    if (!Combatant.TryGetStat<TStat>(out var stat))
      return 0;

    return stat.BaseValue;
  }
}
