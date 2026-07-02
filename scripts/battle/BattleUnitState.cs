using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using FunProject.Combatants;
using FunProject.Items;
using FunProject.Items.Capabilities;
using FunProject.Items.Effects;
using FunProject.Stats;
using FunProject.Weapons;

namespace FunProject.Battle;

public sealed class BattleUnitState
{
  private readonly List<EquippableItem> _inventory;
  private readonly SysColGeneric.HashSet<BattleUnitState> _visibleUnits = [];
  private readonly SysColGeneric.HashSet<BattleBoardState.ValidatedPoint> _visibleTiles = [];
  private readonly SysColGeneric.HashSet<BattleUnitState> _spottedUnits = [];
  private readonly Dictionary<StatusEffectSpecData, ActiveStatusEffect> _activeStatusEffects = [];

  internal int Id { get; }
  public Combatant Combatant { get; }
  public Faction Side => Combatant.OwningFaction;
  public Option<Weapon> EquippedWeapon { get; private set; }
  public Option<ItemWith<ArmorCapability>> EquippedArmor { get; }
  public IReadOnlyList<EquippableItem> Inventory => _inventory;
  internal IReadOnlySet<BattleUnitState> VisibleUnits => _visibleUnits;
  internal IReadOnlySet<BattleBoardState.ValidatedPoint> VisibleTiles => _visibleTiles;

  public int MaxHealth => Mathf.RoundToInt(EffectiveStat<HealthStat>());
  public int CurrentHealth { get; private set; }
  public int MaxActionPoints => Mathf.RoundToInt(EffectiveStat<ActionPointsStat>());
  public int CurrentActionPoints { get; private set; }
  public int Vision => Mathf.RoundToInt(EffectiveStat<VisionStat>());
  public bool IsAlive => CurrentHealth > 0;
  public bool IsDead => !IsAlive;
  public IReadOnlyCollection<ActiveStatusEffect> ActiveStatusEffects => _activeStatusEffects.Values;
  public bool IsImmobilized => _activeStatusEffects.Values.Any(effect => !effect.IsExpired && effect.BlocksAction);

  internal BattleUnitState(
    int unitId,
    Combatant combatant,
    Option<Weapon> equippedWeapon,
    Option<ItemWith<ArmorCapability>> equippedArmor)
  {
    ArgumentOutOfRangeException.ThrowIfLessThan(unitId, 0);

    Id = unitId;
    ArgumentNullException.ThrowIfNull(combatant);
    Combatant = combatant;
    EquippedWeapon = equippedWeapon;
    EquippedArmor = equippedArmor;
    CurrentHealth = MaxHealth;
    CurrentActionPoints = MaxActionPoints;
    // Defensive copy: battle-time inventory mutations must not write through to the
    // shared, authored Combatant template (Data/Runtime ownership boundary).
    _inventory = [.. combatant.Inventory];
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

  internal ActiveStatusEffect ApplyStatusEffect(StatusEffectSpecData spec)
  {
    ArgumentNullException.ThrowIfNull(spec);
    if (_activeStatusEffects.TryGetValue(spec, out var existing))
    {
      existing.Refresh();
      return existing;
    }

    var applied = ActiveStatusEffect.Create(spec);
    _activeStatusEffects[spec] = applied;
    return applied;
  }

  internal bool RemoveStatusEffect(StatusEffectSpecData spec)
  {
    ArgumentNullException.ThrowIfNull(spec);
    return _activeStatusEffects.Remove(spec);
  }

  internal Weapon RequireEquippedWeapon()
  {
    return EquippedWeapon.Match(
      weapon => weapon,
      () => throw new InvalidOperationException($"Unit {Id} has no equipped weapon."));
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

  internal void RemoveVisibleUnit(BattleUnitState unit)
  {
    ArgumentNullException.ThrowIfNull(unit);
    _visibleUnits.Remove(unit);
  }

  internal bool RecordFirstSpotting(BattleUnitState unit)
  {
    ArgumentNullException.ThrowIfNull(unit);
    return _spottedUnits.Add(unit);
  }

  public bool CanAct()
  {
    return CurrentActionPoints > 0 && !IsDead && !IsImmobilized;
  }

  public float EffectiveStat<TStat>() where TStat : Stat
    => ((HasStats)Combatant).Resolve<TStat>(GatherStatContributions());

  public Option<float> TryEffectiveStat<TStat>() where TStat : Stat
    => ((HasStats)Combatant).TryResolve<TStat>(GatherStatContributions());

  private IEnumerable<StatMod> GatherStatContributions()
    => Combatant.StatContributions()
         .Concat(EquippedWeapon.Match(w => w.StatContributions, () => System.Linq.Enumerable.Empty<StatMod>()));
}
