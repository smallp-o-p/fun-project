using System;
using System.Collections.Generic;
using Godot;
using FunProject.Buffs;
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
  private readonly List<(Buff Buff, bool IsActive)> _buffs = [];

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
  public int CurrentStun { get; private set; }
  public int MaxActionPoints => Mathf.RoundToInt(EffectiveStat<ActionPointsStat>());
  public int CurrentActionPoints { get; private set; }
  public int Vision => Mathf.RoundToInt(EffectiveStat<VisionStat>());
  public bool IsAlive => CurrentHealth > 0;
  public bool IsDead => !IsAlive;
  public bool IsUnconscious => IsAlive && CurrentStun >= CurrentHealth;
  public bool IsIncapacitated => IsDead || IsUnconscious || IsImmobilized;
  public IReadOnlyCollection<ActiveStatusEffect> ActiveStatusEffects => _activeStatusEffects.Values;
  public bool IsImmobilized => _activeStatusEffects.Values.AsValueEnumerable().Any(effect => !effect.IsExpired && effect.BlocksAction);
  public IReadOnlyList<Buff> Buffs
    => _buffs.AsValueEnumerable().Select(entry => entry.Buff).ToArray();

  public IEnumerable<Buff> ActiveBuffs
    => _buffs.AsValueEnumerable().Where(entry => entry.IsActive)
      .Select(entry => entry.Buff).ToArray();

  internal IEnumerable<DamageBundleMod> ActiveBuffDamageMods
    => ActiveBuffs.AsValueEnumerable().SelectMany(buff => buff.DamageMods).ToArray();

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
    // Ascending slot order: the battle inventory is positional, and Dictionary enumeration
    // order is an implementation detail — sparse keys must not shuffle it.
    var inventory = new List<EquippableItem>(combatant.MaxInventorySize);
    for (int slot = 0; slot < combatant.MaxInventorySize; slot++)
      if (combatant.Inventory.TryGetValue(slot, out EquippableItem? item))
        inventory.Add(item);
    _inventory = inventory;

    // Preserve every grant in source order, including repeated resources.
    IEnumerable<Buff> granted = combatant.InnateBuffs
      .AsValueEnumerable().Concat(equippedWeapon.Match(w => w.GrantedBuffs, () => (IReadOnlyList<Buff>)[]))
      .Concat(equippedArmor.Match(a => a.Item.GrantedBuffs, () => (IReadOnlyList<Buff>)[]))
      .ToArray();
    foreach (Buff buff in granted)
    {
      ArgumentNullException.ThrowIfNull(buff);
      if (buff.Condition is null)
        throw new InvalidOperationException($"Buff '{buff.Name}' has no activation condition.");

      _buffs.Add((buff, false));
    }
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

  // Trusted-core spend: callers have already proven affordability (ValidateActingUnit), so
  // a failed spend here is a broken invariant — not a rejectable outcome — and throws.
  internal void SpendActionPoints(int cost)
  {
    if (!TrySpendActionPoints(cost))
      throw new InvalidOperationException(
        $"Unit {Id} cannot spend {cost} action points (has {CurrentActionPoints}).");
  }

  public void ReceiveDamage(int amount)
  {
    if (amount <= 0)
      return;

    CurrentHealth = Math.Max(CurrentHealth - amount, 0);
  }

  internal void ReceiveStun(int amount)
  {
    ArgumentOutOfRangeException.ThrowIfNegative(amount);
    CurrentStun = checked(CurrentStun + amount);
  }

  internal uint RecoverStun(uint maximum)
  {
    if (IsDead || IsUnconscious)
      return 0;
    uint recovered = Math.Min((uint)CurrentStun, maximum);
    CurrentStun -= (int)recovered;
    return recovered;
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

  internal void ClearVisibleUnits()
  {
    _visibleUnits.Clear();
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

  internal bool RecordFirstSpotting(BattleUnitState unit)
  {
    ArgumentNullException.ThrowIfNull(unit);
    return _spottedUnits.Add(unit);
  }

  public bool CanAct() => CurrentActionPoints > 0 && !IsIncapacitated;

  public float EffectiveStat<TStat>() where TStat : Stat
    => Combatant.Resolve<TStat>(GatherStatContributions());

  public Option<float> TryEffectiveStat<TStat>() where TStat : Stat
    => Combatant.TryResolve<TStat>(GatherStatContributions());

  private IEnumerable<StatMod> GatherStatContributions()
    => Combatant.StatContributions()
         .AsValueEnumerable()
         .Concat(EquippedWeapon.Match<IEnumerable<StatMod>>(w => w.StatContributions, () => System.Array.Empty<StatMod>()))
         .Concat(ActiveBuffs.AsValueEnumerable().SelectMany(buff => buff.StatMods).ToArray())
         .ToArray();

  internal void ClampCurrentHealthToMax()
  {
    // Floor at 1: buffs adjust stats, they never kill. A buff pushing MaxHealth to 0 or
    // below must not bypass the death pipeline (HandleUnitDeath) by zeroing health here.
    CurrentHealth = Math.Min(CurrentHealth, Math.Max(MaxHealth, 1));
  }

  internal void EvaluateBuffs(BattleSession session)
  {
    for (int i = 0; i < _buffs.Count; i++)
    {
      (Buff buff, bool wasActive) = _buffs[i];
      bool isActive = buff.Condition.IsMet(session, this);
      if (isActive == wasActive)
        continue;

      _buffs[i] = (buff, isActive);
      ClampCurrentHealthToMax();
      session.RaiseEvents(isActive
        ? new UnitBuffActivatedBattleEvent(this, buff)
        : new UnitBuffDeactivatedBattleEvent(this, buff));
    }
  }
}
