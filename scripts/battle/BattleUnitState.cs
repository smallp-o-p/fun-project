#nullable enable
using System;
using FunProject.Combatants;
using FunProject.Stats;
using FunProject.Weapons;
using Godot;

namespace FunProject.Battle;

public sealed class BattleUnitState
{
  public int UnitId { get; }
  public Combatant Combatant { get; }
  public Faction Side => Combatant.OwningFaction;
  public Vector3I Position { get; private set; }
  public Weapon? EquippedWeapon { get; private set; }
  public bool IsSelected { get; internal set; }

  public int MaxHealth => GetBaseStatValue(StatType.Health);
  public int CurrentHealth { get; private set; }
  public int MaxActionPoints => GetBaseStatValue(StatType.ActionPoints);
  public int CurrentActionPoints { get; private set; }
  public int Movement => GetBaseStatValue(StatType.Movement);
  public bool IsAlive => CurrentHealth > 0;
  public bool CanAct => IsAlive && CurrentActionPoints > 0;

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

  private int GetBaseStatValue(StatType statType)
  {
    if (!Combatant.GetStats().TryGetValue(statType, out var stat))
      return 0;

    return stat.BaseValue;
  }
}
