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
  private readonly BattleAccessBinding _battleAccess = new();
  internal void BindBattleAccess(BattleAccessGuard access)
  {
    _battleAccess.Bind(access);
    Combatant.BindBattleAccess(access);
    EquippedWeapon.IfSome(weapon => weapon.BindBattleAccess(access));
    EquippedArmor.IfSome(armor => armor.Item.BindBattleAccess(access));
    foreach (var item in _inventory) item.BindBattleAccess(access);
    foreach (var status in _activeStatusEffects.Values) status.BindBattleAccess(access);
    PrepareStatInputs();
  }
  internal void UnbindBattleAccess(BattleAccessGuard access) => _battleAccess.Unbind(access);

  private readonly List<EquippableItem> _inventory;
  private readonly StatMod[] _loadoutStatMods;
  private BattleStatInputs? _statInputs;
  internal void PrepareStatInputs()
  {
    _battleAccess.RequireMutation();
    _statInputs = BattleStatInputs.Capture(Combatant.CopyBaseValues(),
      Combatant.StatContributions().AsValueEnumerable().Concat(_loadoutStatMods)
        .Concat(EquippedWeapon.Match<IEnumerable<StatMod>>(weapon => weapon.StatContributions, () => []))
        .ToArray(), Buffs);
  }
  private readonly SysColGeneric.HashSet<BattleUnitState> _visibleUnits = [];
  private readonly SysColGeneric.HashSet<BattleBoardState.ValidatedPoint> _visibleTiles = [];
  private readonly SysColGeneric.HashSet<BattleUnitState> _spottedUnits = [];
  private bool _hasSeenHostile;
  internal bool HasSeenHostile { get { _battleAccess.RequireRead(); return _hasSeenHostile; } }
  internal void RecordHostileContact() { _battleAccess.RequireMutation(); _hasSeenHostile = true; }
  internal bool CanSeeTile(BattleBoardState.ValidatedPoint tile) { _battleAccess.RequireRead(); return _visibleTiles.Contains(tile); }
  private readonly Dictionary<StatusEffectSpecData, ActiveStatusEffect> _activeStatusEffects = [];
  private readonly List<(Buff Buff, bool IsActive)> _buffs = [];

  public int Id { get; }
  public Combatant Combatant { get; }
  public Faction Side => Combatant.OwningFaction;
  private Option<Weapon> _equippedWeapon;
  public Option<Weapon> EquippedWeapon
  {
    get { _battleAccess.RequireRead(); return _equippedWeapon; }
    private set { _battleAccess.RequireMutation(); _equippedWeapon = value; }
  }
  public Option<ItemWith<ArmorCapability>> EquippedArmor { get; }
  public IReadOnlyList<EquippableItem> Inventory { get { _battleAccess.RequireRead(); return _inventory.AsReadOnly(); } }
  internal IReadOnlySet<BattleUnitState> VisibleUnits { get { _battleAccess.RequireRead(); return _visibleUnits.AsValueEnumerable().ToHashSet(); } }
  internal IReadOnlySet<BattleBoardState.ValidatedPoint> VisibleTiles { get { _battleAccess.RequireRead(); return _visibleTiles.AsValueEnumerable().ToHashSet(); } }

  public int MaxHealth => Mathf.RoundToInt(EffectiveStat<HealthStat>());
  private int _currentHealth;
  public int CurrentHealth
  {
    get { _battleAccess.RequireRead(); return _currentHealth; }
    private set { _battleAccess.RequireMutation(); _currentHealth = value; }
  }
  private long _totalHealthDamageTaken;
  public long TotalHealthDamageTaken
  {
    get { _battleAccess.RequireRead(); return _totalHealthDamageTaken; }
    private set { _battleAccess.RequireMutation(); _totalHealthDamageTaken = value; }
  }
  private int _currentStun;
  public int CurrentStun
  {
    get { _battleAccess.RequireRead(); return _currentStun; }
    private set { _battleAccess.RequireMutation(); _currentStun = value; }
  }
  public int MaxActionPoints => Mathf.RoundToInt(EffectiveStat<ActionPointsStat>());
  private int _currentActionPoints;
  public int CurrentActionPoints
  {
    get { _battleAccess.RequireRead(); return _currentActionPoints; }
    private set { _battleAccess.RequireMutation(); _currentActionPoints = value; }
  }
  public int Vision => Mathf.RoundToInt(EffectiveStat<VisionStat>());
  public bool IsAlive => CurrentHealth > 0;
  public bool IsDead => !IsAlive;
  public bool IsUnconscious => IsAlive && CurrentStun >= CurrentHealth;
  public bool IsIncapacitated => IsDead || IsUnconscious || IsImmobilized;
  public IReadOnlyCollection<ActiveStatusEffect> ActiveStatusEffects { get { _battleAccess.RequireRead(); return _activeStatusEffects.Values.AsValueEnumerable().ToArray(); } }
  public bool IsImmobilized { get { _battleAccess.RequireRead(); return _activeStatusEffects.Values.AsValueEnumerable().Any(effect => !effect.IsExpired && effect.BlocksAction); } }
  public IReadOnlyList<Buff> Buffs
  { get { _battleAccess.RequireRead(); return _buffs.AsValueEnumerable().Select(entry => entry.Buff).ToArray(); } }

  public IEnumerable<Buff> ActiveBuffs
  {
    get
    {
      _battleAccess.RequireRead(); return _buffs.AsValueEnumerable().Where(entry => entry.IsActive)
    .Select(entry => entry.Buff).ToArray();
    }
  }

  internal IEnumerable<DamageBundleMod> ActiveBuffDamageMods
  { get { _battleAccess.RequireRead(); return ActiveBuffs.AsValueEnumerable().SelectMany(buff => buff.DamageMods).ToArray(); } }

  internal BattleUnitState(
    int unitId,
    Combatant combatant,
    Option<Weapon> equippedWeapon,
    Option<ItemWith<ArmorCapability>> equippedArmor,
    IReadOnlyList<StatMod>? statMods = null)
  {
    ArgumentOutOfRangeException.ThrowIfLessThan(unitId, 0);

    Id = unitId;
    ArgumentNullException.ThrowIfNull(combatant);
    Combatant = combatant;
    EquippedWeapon = equippedWeapon;
    EquippedArmor = equippedArmor;
    // Capture the supplied modifier membership once at spawn (deployment-time snapshot),
    // before the initial health/AP reads below.
    _loadoutStatMods = [.. statMods ?? []];
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
    _battleAccess.RequireMutation();
    CurrentActionPoints = MaxActionPoints;
  }

  public bool TrySpendActionPoints(int cost)
  {
    _battleAccess.RequireMutation();
    if (cost < 0 || CurrentActionPoints < cost)
      return false;

    CurrentActionPoints -= cost;
    return true;
  }

  // Trusted-core spend: callers have already proven affordability (ValidateActingUnit), so
  // a failed spend here is a broken invariant — not a rejectable outcome — and throws.
  internal void SpendActionPoints(int cost)
  {
    _battleAccess.RequireMutation();
    if (!TrySpendActionPoints(cost))
      throw new InvalidOperationException(
        $"Unit {Id} cannot spend {cost} action points (has {CurrentActionPoints}).");
  }

  public void ReceiveDamage(int amount)
  {
    _battleAccess.RequireMutation();
    if (amount <= 0)
      return;

    // Only health actually lost counts toward the mission's damage metrics: overkill past
    // zero never inflates the campaign's injury bookkeeping.
    int actualLoss = Math.Min(amount, CurrentHealth);
    TotalHealthDamageTaken = checked(TotalHealthDamageTaken + actualLoss);
    CurrentHealth -= actualLoss;
  }

  internal void ReceiveStun(int amount)
  {
    _battleAccess.RequireMutation();
    ArgumentOutOfRangeException.ThrowIfNegative(amount);
    CurrentStun = checked(CurrentStun + amount);
  }

  internal uint RecoverStun(uint maximum)
  {
    _battleAccess.RequireMutation();
    if (IsDead || IsUnconscious)
      return 0;
    uint recovered = Math.Min((uint)CurrentStun, maximum);
    CurrentStun -= (int)recovered;
    return recovered;
  }

  internal ActiveStatusEffect ApplyStatusEffect(StatusEffectSpecData spec)
  {
    _battleAccess.RequireMutation();
    ArgumentNullException.ThrowIfNull(spec);
    if (_activeStatusEffects.TryGetValue(spec, out var existing))
    {
      existing.Refresh();
      return existing;
    }

    var applied = ActiveStatusEffect.Create(spec);
    if (_battleAccess.Guard is { } access) applied.BindBattleAccess(access);
    _activeStatusEffects[spec] = applied;
    return applied;
  }

  internal bool RemoveStatusEffect(StatusEffectSpecData spec)
  {
    _battleAccess.RequireMutation();
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
    _battleAccess.RequireMutation();
    if (_battleAccess.Guard is { } access) item.BindBattleAccess(access);
    _inventory.Add(item);
  }

  public bool HasInventoryItem(EquippableItem item)
  {
    _battleAccess.RequireRead();
    return _inventory.Contains(item);
  }

  public bool RemoveInventoryItem(EquippableItem item)
  {
    _battleAccess.RequireMutation();
    return _inventory.Remove(item);
  }

  internal void ClearVisibility()
  {
    _battleAccess.RequireMutation();
    _visibleUnits.Clear();
    _visibleTiles.Clear();
  }

  internal void ClearVisibleUnits()
  {
    _battleAccess.RequireMutation();
    _visibleUnits.Clear();
  }

  internal void AddVisibleTile(BattleBoardState.ValidatedPoint tile)
  {
    _battleAccess.RequireMutation();
    _visibleTiles.Add(tile);
  }

  internal void AddVisibleUnit(BattleUnitState unit)
  {
    _battleAccess.RequireMutation();
    ArgumentNullException.ThrowIfNull(unit);
    _visibleUnits.Add(unit);
  }

  internal bool RecordFirstSpotting(BattleUnitState unit)
  {
    _battleAccess.RequireMutation();
    ArgumentNullException.ThrowIfNull(unit);
    return _spottedUnits.Add(unit);
  }

  public bool CanAct() { _battleAccess.RequireRead(); return CurrentActionPoints > 0 && !IsIncapacitated; }

  public float EffectiveStat<TStat>() where TStat : Stat
  { _battleAccess.RequireRead(); return TryEffectiveStat<TStat>().IfNone(0f); }

  public Option<float> TryEffectiveStat<TStat>() where TStat : Stat
  {
    _battleAccess.RequireRead(); return _battleAccess.Guard is null
    ? Combatant.TryResolve<TStat>(GatherStatContributions())
    : (_statInputs ?? throw new InvalidOperationException("Bound unit inputs are not prepared.")).TryResolve<TStat>(ActiveBuffs);
  }

  private IEnumerable<StatMod> GatherStatContributions()
    => Combatant.StatContributions()
         .AsValueEnumerable()
         .Concat(_loadoutStatMods)
         .Concat(EquippedWeapon.Match<IEnumerable<StatMod>>(w => w.StatContributions, () => System.Array.Empty<StatMod>()))
         .Concat(ActiveBuffs.AsValueEnumerable().SelectMany(buff => buff.StatMods).ToArray())
         .ToArray();

  internal void ClampCurrentHealthToMax()
  {
    _battleAccess.RequireMutation();
    // Floor at 1: buffs adjust stats, they never kill. A buff pushing MaxHealth to 0 or
    // below must not bypass the death pipeline (HandleUnitDeath) by zeroing health here.
    CurrentHealth = Math.Min(CurrentHealth, Math.Max(MaxHealth, 1));
  }

  internal void EvaluateBuffs(BattleSession session)
  {
    _battleAccess.RequireMutation();
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
