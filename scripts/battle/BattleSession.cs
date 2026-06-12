using FunProject.Combatants;
using FunProject.Core;
using FunProject.Items;
using FunProject.Items.Capabilities;
using FunProject.Items.Effects;
using FunProject.Weapons;
using Godot;
using LanguageExt.UnsafeValueAccess;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FunProject.Battle;

public enum BattlePhase
{
  Setup,
  InProgress,
  Ended,
}

public sealed class BattleSession
{
  internal readonly record struct SpawnedBattleUnit(BattleUnitState Unit);

  public const int DefaultMovementStepActionPointCost = 1;
  public const int DefaultAttackActionPointCost = 1;
  private static readonly IReadOnlySet<BattleBoardState.ValidatedPoint> EmptyTileSet = new SysColGeneric.HashSet<BattleBoardState.ValidatedPoint>();
  private static readonly Vector3I[] OrthogonalDirections =
  [
    new(1, 0, 0),
    new(-1, 0, 0),
    new(0, 0, 1),
    new(0, 0, -1),
  ];
  private static readonly Vector3I[] AdjacentDiagonalOffsets =
  [
    new(-1, 0, -1),
    new(-1, 0, 1),
    new(1, 0, -1),
    new(1, 0, 1),
  ];

  private readonly IHitChanceCalculator _hitChanceCalculator;
  private readonly Random _random;
  private readonly List<BattleUnitState> _units = [];
  private readonly Dictionary<Faction, SysColGeneric.HashSet<BattleBoardState.ValidatedPoint>> _exploredTilesByFaction = [];
  private readonly Queue<Faction> _globalFactionOrder = [];
  private Queue<Faction> _turnQueue = [];
  private readonly SysColGeneric.HashSet<Faction> _sidesActedThisRound = [];
  private readonly SysColGeneric.HashSet<BattleUnitState> _activeFactionUnitsAvailable = [];
  private readonly BattleEventListenerRegistry _listenerRegistry = new();
  private readonly Queue<BattleEvent> _eventDispatchQueue = [];
  private bool _isDispatchingEvents;

  public BattleBoardState Board { get; }
  public BattlePhase Phase { get; private set; } = BattlePhase.Setup;
  public int TurnNumber { get; private set; } = 1;
  public Faction ActiveSide { get; private set; }
  public IEnumerable<BattleUnitState> AliveUnits => _units.Where((unit) => unit.IsAlive);
  public IEnumerable<BattleUnitState> DeadUnits => _units.Where((unit) => unit.IsDead);
  public IReadOnlyCollection<Faction> GlobalFactionTurnOrder => _globalFactionOrder;
  public IReadOnlyCollection<Faction> TurnQueue => _turnQueue;

  public event Action<BattleEvent> BattleEventCommitted = delegate { };

  public BattleSession(
    BattleBoardState board,
    IEnumerable<Faction> globalFactionOrder,
    IHitChanceCalculator? hitChanceCalculator = null,
    int? randomSeed = null)
  {
    ArgumentNullException.ThrowIfNull(board);
    ArgumentNullException.ThrowIfNull(globalFactionOrder);

    _hitChanceCalculator = hitChanceCalculator ?? new StandardHitChanceCalculator();
    _random = randomSeed is null ? new Random() : new Random(randomSeed.Value);

    Board = board;

    foreach (var faction in globalFactionOrder)
    {
      EnqueueFactionInGlobalOrder(faction);
    }

    if (_globalFactionOrder.Count == 0)
      throw new ArgumentException("Battle session requires at least one faction in the global order.");

    _turnQueue = new Queue<Faction>(_globalFactionOrder);
    ActiveSide = _turnQueue.Peek();
    // Status ticks run before armor regen: a DoT tick re-arms the regen delay and
    // suppresses that same turn's shield recharge.
    RegisterListener<TurnEndedBattleEvent>(new StatusEffectSystem());
    RegisterListener<TurnEndedBattleEvent>(new ArmorRegenSystem());
  }

  internal IEnumerable<BattleUnitState> GetFactionAliveUnits(Faction side)
  {
    ArgumentNullException.ThrowIfNull(side);
    return AliveUnits.Where(unit => unit.Side == side);
  }

  internal IReadOnlySet<BattleBoardState.ValidatedPoint> GetFactionVisibleTiles(Faction side)
  {
    ArgumentNullException.ThrowIfNull(side);
    return GetFactionAliveUnits(side)
      .SelectMany(unit => unit.VisibleTiles)
      .ToHashSet();
  }

  internal IReadOnlySet<BattleBoardState.ValidatedPoint> GetFactionExploredTiles(Faction side)
  {
    ArgumentNullException.ThrowIfNull(side);
    return _exploredTilesByFaction.TryGetValue(side, out var tiles) ? tiles : EmptyTileSet;
  }

  internal bool IsUnitVisibleToFaction(Faction side, BattleUnitState target)
  {
    ArgumentNullException.ThrowIfNull(side);
    ArgumentNullException.ThrowIfNull(target);

    if (target.Side == side)
      return true;

    return GetFactionAliveUnits(side)
      .Any(unit => unit.VisibleUnits.Contains(target));
  }

  internal bool IsTileVisibleToFaction(Faction side, BattleBoardState.ValidatedPoint tile)
  {
    ArgumentNullException.ThrowIfNull(side);
    return GetFactionAliveUnits(side).Any(unit => unit.VisibleTiles.Contains(tile));
  }

  internal bool IsUnitStillAvailableThisTurn(BattleUnitState unit)
  {
    ArgumentNullException.ThrowIfNull(unit);
    return _activeFactionUnitsAvailable.Contains(unit);
  }

  internal void StartBattle()
  {
    if (Phase != BattlePhase.Setup)
      throw new InvalidOperationException("Battle session can only be started from setup.");

    RebuildRoundQueueFromLivingSides();
    if (_turnQueue.Count == 0)
      throw new InvalidOperationException("Cannot start a battle without at least one living faction in the session.");

    Phase = BattlePhase.InProgress;
    TurnNumber = 1;
    ActiveSide = _turnQueue.Peek();
    _sidesActedThisRound.Clear();

    foreach (var unit in AliveUnits)
      unit.RefreshForNewTurn();

    RefreshCurrentFactionAvailability();

    RaiseEvents(
      new SessionStartedBattleEvent(),
      new TurnStartedBattleEvent(ActiveSide, TurnNumber));
  }

  internal SpawnedBattleUnit AddUnit(
    Combatant combatant,
    BattleBoardState.ValidatedPoint position,
    Option<Weapon> equippedWeapon,
    Option<ItemWith<ArmorCapability>> equippedArmor)
  {
    ArgumentNullException.ThrowIfNull(combatant);
    if (Phase == BattlePhase.Ended)
      throw new InvalidOperationException("Cannot add units after the battle has ended.");

    var unit = new BattleUnitState(_units.Count, combatant, equippedWeapon, equippedArmor);
    bool occupantSet = Board.TryPlaceOccupant(position, unit.Id);
    if (!occupantSet)
      throw new InvalidOperationException($"Could not place unit {unit.Id} at {position.Raw}.");

    _units.Add(unit);
    EnqueueFactionInGlobalOrder(unit.Side);

    if (Phase == BattlePhase.Setup && !_turnQueue.Contains(unit.Side))
      _turnQueue.Enqueue(unit.Side);
    if (Phase == BattlePhase.InProgress)
      AddSpawnedUnitToCurrentRound(unit);

    RaiseEvent(new UnitAddedBattleEvent(unit, position));

    return new SpawnedBattleUnit(unit);
  }

  internal void ApplyDamageTo(BattleUnitState unit, int amount)
    => ApplyDamageTo(unit, [new Damage(amount, Element.Kinetic)]);

  internal void ApplyDamageTo(BattleUnitState unit, IReadOnlyList<Damage> bundle)
  {
    ArgumentNullException.ThrowIfNull(unit);
    ArgumentNullException.ThrowIfNull(bundle);
    if (unit.IsDead)
      throw new InvalidOperationException($"Cannot damage unit {unit.Id} because it is already dead.");
    if (Board.FindOccupantPosition(unit.Id).IsNone)
      throw new InvalidOperationException($"Cannot damage unit {unit.Id} because it is not on the board.");

    Option<ArmorState> armorState = unit.EquippedArmor.Map(
      armor => new ArmorState(armor.Capability.Current, armor.Capability.Element));
    IReadOnlyList<PacketResolution> packetResolutions = DamageResolver.ResolvePackets(bundle, armorState);
    DamageResolution resolution = DamageResolver.Resolve(packetResolutions);

    unit.EquippedArmor.IfSome(armor =>
    {
      armor.Capability.Reduce(resolution.ArmorDamage);
      if (resolution.ArmorDamage > 0 || resolution.HealthDamage > 0)
        armor.Capability.RearmRegenDelay();
    });
    unit.ReceiveDamage(resolution.HealthDamage);

    RaiseEvent(new UnitDamagedBattleEvent(unit, bundle, resolution.ArmorDamage, resolution.HealthDamage));
    if (unit.IsDead)
    {
      HandleUnitDeath(unit);
      return;
    }

    ApplyStatusEffectsFrom(unit, bundle, packetResolutions);
  }

  private void ApplyStatusEffectsFrom(
    BattleUnitState unit,
    IReadOnlyList<Damage> bundle,
    IReadOnlyList<PacketResolution> packetResolutions)
  {
    foreach ((Damage damage, PacketResolution resolution) in bundle.Zip(packetResolutions))
    {
      if (damage.Amount <= 0)
        continue;

      damage.Status.IfSome(spec =>
      {
        if (spec.RequiresHealthDamage && resolution.HealthDamage <= 0)
          return;
        if (spec.ApplyChancePercent < 100 && RollPercent() >= spec.ApplyChancePercent)
          return;

        ActiveStatusEffect applied = unit.ApplyStatusEffect(spec);
        RaiseEvent(new UnitStatusEffectAppliedBattleEvent(unit, spec, applied.RemainingTurns));
      });
    }
  }

  private void HandleUnitDeath(BattleUnitState unit)
  {
    ArgumentNullException.ThrowIfNull(unit);
    if (unit.IsAlive)
      throw new InvalidOperationException($"Cannot remove unit {unit.Id} as dead because it is still alive.");

    var unitSide = unit.Side;
    Option<BattleBoardState.ValidatedPoint> unitPointOption = Board.FindOccupantPosition(unit.Id);
    if (unitPointOption.IsNone)
      throw new InvalidOperationException($"Could not clear unit {unit.Id} because it is not on the board.");
    BattleBoardState.ValidatedPoint unitPoint = unitPointOption.Value();

    bool occupantCleared = Board.TryClearOccupant(unitPoint, unit.Id);
    if (!occupantCleared)
      throw new InvalidOperationException($"Could not clear unit {unit.Id} from {unitPoint.Raw}.");

    _activeFactionUnitsAvailable.Remove(unit);
    RaiseEvent(new UnitKilledBattleEvent(unit, unitPoint));
    HandleFactionLoss(unitSide);
  }

  internal void EndUnitActivation(BattleUnitState unit)
  {
    ArgumentNullException.ThrowIfNull(unit);
    if (Phase != BattlePhase.InProgress)
      throw new InvalidOperationException("Cannot end a unit activation while the battle is not in progress.");

    var activeSide = ActiveSide;
    if (unit.Side != activeSide)
      throw new InvalidOperationException($"Unit {unit.Id} is not on the active side.");

    Option<BattleBoardState.ValidatedPoint> unitPointOption = GetUnitPosition(unit);
    if (unitPointOption.IsNone)
      throw new InvalidOperationException($"Cannot end activation for unit {unit.Id} because it is not on the board.");
    BattleBoardState.ValidatedPoint unitPoint = unitPointOption.Value();

    if (!_activeFactionUnitsAvailable.Remove(unit))
      throw new InvalidOperationException($"Unit {unit.Id} is not available this turn.");

    RaiseEvent(new UnitActivationEndedBattleEvent(unit, unitPoint));

    if (!GetFactionAliveUnits(activeSide).Any(CanUnitActNow))
      EndFactionTurn(activeSide);
  }

  private void AdvanceTurn()
  {
    RaiseEvent(new TurnEndedBattleEvent(
      ActiveSide,
      TurnNumber));
    _sidesActedThisRound.Add(ActiveSide);

    _turnQueue.Dequeue();

    RemoveEliminatedSidesFromQueue();
    if (_turnQueue.Count == 0)
    {
      StartNextRound();
      return;
    }

    BeginNextQueuedSideTurn();
  }

  internal void EndFactionTurn(Faction expectedActiveSide)
  {
    if (Phase != BattlePhase.InProgress)
      throw new InvalidOperationException("Cannot end a faction turn while the battle is not in progress.");

    var activeSide = ActiveSide;
    if (activeSide != expectedActiveSide)
      throw new InvalidOperationException($"{expectedActiveSide.Name} cannot end a turn while {activeSide.Name} is active.");

    AdvanceTurn();
  }

  private void EndBattle()
  {
    _activeFactionUnitsAvailable.Clear();
    _turnQueue.Clear();
    Phase = BattlePhase.Ended;

    RaiseEvent(new SessionEndedBattleEvent());
  }

  private void AddSpawnedUnitToCurrentRound(BattleUnitState unit)
  {
    if (Phase != BattlePhase.InProgress)
      throw new InvalidOperationException("Trying to spawn unit while battle hasn't started or is done.");
    if (unit.Side == ActiveSide)
    {
      _activeFactionUnitsAvailable.Add(unit);
      return;
    }

    if (_sidesActedThisRound.Contains(unit.Side))
      return;
    if (_turnQueue.Contains(unit.Side))
      return;

    _turnQueue.Enqueue(unit.Side);
  }

  internal Option<BattleBoardState.ValidatedPoint> GetUnitPosition(BattleUnitState unit)
  {
    ArgumentNullException.ThrowIfNull(unit);
    return Board.FindOccupantPosition(unit.Id);
  }

  internal Option<BattleUnitState> GetUnitAt(BattleBoardState.ValidatedPoint point)
  {
    return Board.GetOccupant(point).Bind(id =>
      id < _units.Count && _units[id].IsAlive ? Some(_units[id]) : None);
  }

  internal void MoveUnit(BattleUnitState unit, BattleBoardState.ValidatedPoint source, BattleBoardState.ValidatedPoint destination)
  {
    ArgumentNullException.ThrowIfNull(unit);
    if (unit.IsDead)
      throw new InvalidOperationException($"Cannot move unit {unit.Id} because it is dead.");
    var boardPosition = Board.FindOccupantPosition(unit.Id);
    if (boardPosition.IsNone)
      throw new InvalidOperationException($"Unit {unit.Id} is not tracked in the session position index.");
    if (boardPosition.Value() != source)
      throw new InvalidOperationException($"Unit {unit.Id} is indexed at {boardPosition.Value().Raw}, not {source.Raw}.");
    if (!Board.TryMoveOccupant(source, destination, unit.Id))
      throw new InvalidOperationException($"Could not move unit {unit.Id} from {source.Raw} to {destination.Raw}.");

    RaiseEvents(
      new UnitMovedBattleEvent(unit, destination, source),
      new TileOccupiedBattleEvent(unit, destination));
  }

  internal bool CanUnitActNow(BattleUnitState unit)
  {
    if (Phase != BattlePhase.InProgress)
      throw new InvalidOperationException("Trying to check unit while the battle is not in progress.");

    if (unit.Side != ActiveSide)
      return false;
    if (!_activeFactionUnitsAvailable.Contains(unit))
      return false;

    return unit.CurrentActionPoints > 0 && !unit.IsImmobilized;
  }

  internal static int GetGridDistance(Vector3I source, Vector3I destination)
  {
    return BattleBoardState.GetGridDistance(source, destination);
  }

  internal IHitChanceCalculator HitChanceCalculator => _hitChanceCalculator;

  internal int RollPercent()
  {
    return _random.Next(100);
  }

  private bool HasLivingUnits(Faction side)
  {
    return GetFactionAliveUnits(side).Any();
  }

  private void RefreshCurrentFactionAvailability()
  {
    _activeFactionUnitsAvailable.Clear();
    foreach (var unit in GetFactionAliveUnits(ActiveSide))
      _activeFactionUnitsAvailable.Add(unit);
  }

  internal bool IsDispatchingEvents => _isDispatchingEvents;

  internal void RegisterListener<TEventKey>(BattleEventListener listener)
    where TEventKey : BattleEventTag
  {
    _listenerRegistry.Register<TEventKey>(listener);
  }

  internal void RaiseEvent(BattleEvent battleEvent)
  {
    ArgumentNullException.ThrowIfNull(battleEvent);
    _eventDispatchQueue.Enqueue(battleEvent);
    DispatchQueuedEvents();
  }

  private void RaiseEvents(params BattleEvent[] events)
  {
    foreach (var battleEvent in events)
    {
      ArgumentNullException.ThrowIfNull(battleEvent);
      _eventDispatchQueue.Enqueue(battleEvent);
    }

    DispatchQueuedEvents();
  }

  private void DispatchQueuedEvents()
  {
    if (_isDispatchingEvents)
      return;

    _isDispatchingEvents = true;
    try
    {
      while (_eventDispatchQueue.Count > 0)
      {
        BattleEvent battleEvent = _eventDispatchQueue.Dequeue();
        // Per-event refresh is deliberate: a listener may mutate state between events.
        RefreshVisibility();
        BattleEventCommitted.Invoke(battleEvent);
        foreach (BattleEventListener listener in _listenerRegistry.GetMatchingListeners(battleEvent))
          listener.OnEventCommitted(this, battleEvent);
      }
    }
    catch
    {
      // A throwing observer aborts the drain; stale events must not dispatch later.
      _eventDispatchQueue.Clear();
      throw;
    }
    finally
    {
      _isDispatchingEvents = false;
    }
  }

  private void RefreshVisibility()
  {
    foreach (var unit in _units)
      unit.ClearVisibility();

    foreach (var observer in AliveUnits)
    {
      var observerPositionOption = Board.FindOccupantPosition(observer.Id);
      if (observerPositionOption.IsNone)
        throw new InvalidOperationException($"Living unit {observer.Id} is missing from the session position index.");
      var observerPosition = observerPositionOption.Value();

      SysColGeneric.HashSet<BattleBoardState.ValidatedPoint> observerVisibleTiles = GetVisibleTiles(observer, observerPosition);
      foreach (var visibleTile in observerVisibleTiles)
        observer.AddVisibleTile(visibleTile);

      MarkTilesExplored(observer.Side, observerVisibleTiles);

      foreach (var visibleTile in observerVisibleTiles)
      {
        var occupantId = Board.GetOccupant(visibleTile);
        if (occupantId.IsNone)
          continue;
        var target = _units[occupantId.Value()];
        if (ReferenceEquals(target, observer) || target.IsDead)
          continue;

        observer.AddVisibleUnit(target);
      }
    }
  }

  private void MarkTilesExplored(Faction side, IEnumerable<BattleBoardState.ValidatedPoint> tiles)
  {
    ArgumentNullException.ThrowIfNull(side);
    ArgumentNullException.ThrowIfNull(tiles);

    if (!_exploredTilesByFaction.TryGetValue(side, out var exploredTiles))
    {
      exploredTiles = [];
      _exploredTilesByFaction.Add(side, exploredTiles);
    }

    exploredTiles.UnionWith(tiles);
  }

  private SysColGeneric.HashSet<BattleBoardState.ValidatedPoint> GetVisibleTiles(
    BattleUnitState observer,
    BattleBoardState.ValidatedPoint observerPosition)
  {
    ArgumentNullException.ThrowIfNull(observer);

    Queue<BattleBoardState.ValidatedPoint> frontier = new([observerPosition]);
    SysColGeneric.HashSet<BattleBoardState.ValidatedPoint> visited = [];
    SysColGeneric.HashSet<BattleBoardState.ValidatedPoint> visibleTiles = [];

    while (frontier.Count > 0)
    {
      var current = frontier.Dequeue();
      if (!visited.Add(current))
        continue;
      if (!IsWithinSameLevelVisionRange(observerPosition, current, observer.Vision))
        continue;

      visibleTiles.Add(current);
      bool currentBlocksLineOfSight = current != observerPosition && Board.GetTile(current).BlocksLineOfSight;
      if (!currentBlocksLineOfSight)
      {
        AddAdjacentDiagonalVisibleTiles(observerPosition, current, observer.Vision, visibleTiles);
        foreach (var neighbor in EnumerateOrthogonalNeighbors(current))
          frontier.Enqueue(neighbor);
      }
    }

    return visibleTiles;
  }

  private IEnumerable<BattleBoardState.ValidatedPoint> EnumerateOrthogonalNeighbors(BattleBoardState.ValidatedPoint point)
  {
    foreach (var direction in OrthogonalDirections)
    {
      var neighborOption = Board.ValidatePoint(point.Raw + direction);
      if (neighborOption.IsSome)
        yield return neighborOption.Value();
    }
  }

  private void AddAdjacentDiagonalVisibleTiles(
    BattleBoardState.ValidatedPoint observerPosition,
    BattleBoardState.ValidatedPoint visibleTile,
    int vision,
    SysColGeneric.HashSet<BattleBoardState.ValidatedPoint> visibleTiles)
  {
    foreach (var offset in AdjacentDiagonalOffsets)
    {
      Board.ValidatePoint(visibleTile.Raw + offset).IfSome((diagonal) =>
      {
        if (IsWithinSameLevelVisionRange(observerPosition, diagonal, vision) && HasOpenDiagonalSide(visibleTile, offset))
          visibleTiles.Add(diagonal);
      });
    }
  }

  private bool HasOpenDiagonalSide(BattleBoardState.ValidatedPoint source, Vector3I diagonalOffset)
  {
    return IsOpenSide(source.Raw + new Vector3I(diagonalOffset.X, 0, 0))
      || IsOpenSide(source.Raw + new Vector3I(0, 0, diagonalOffset.Z));
  }

  private bool IsOpenSide(Vector3I coordinates)
  {
    return Board.ValidatePoint(coordinates).Match(
      side => !Board.GetTile(side).BlocksLineOfSight,
      () => false);
  }

  private static bool IsWithinSameLevelVisionRange(
    BattleBoardState.ValidatedPoint observerPosition,
    BattleBoardState.ValidatedPoint target,
    int vision)
  {
    if (target == observerPosition)
      return true;
    if (vision < 0 || target.Y != observerPosition.Y)
      return false;

    Vector3I delta = target.Raw - observerPosition.Raw;
    return delta.LengthSquared() <= vision * vision;
  }

  private void RebuildRoundQueueFromLivingSides()
  {
    _turnQueue.Clear();

    foreach (var side in _globalFactionOrder)
    {
      if (HasLivingUnits(side))
        _turnQueue.Enqueue(side);
    }
  }

  private void RemoveEliminatedSidesFromQueue()
  {
    _turnQueue = new Queue<Faction>(_turnQueue.Where(HasLivingUnits));
  }

  private void EnqueueFactionInGlobalOrder(Faction side)
  {
    if (_globalFactionOrder.Contains(side))
      return;

    _globalFactionOrder.Enqueue(side);
  }

  private void HandleFactionLoss(Faction side)
  {
    if (HasLivingUnits(side))
      return;

    _sidesActedThisRound.Remove(side);
    if (Phase != BattlePhase.InProgress || ActiveSide != side)
    {
      RemoveSideFromQueue(side);
      return;
    }

    _activeFactionUnitsAvailable.Clear();
  }

  private void RemoveSideFromQueue(Faction side)
  {
    _turnQueue = new Queue<Faction>(_turnQueue.Where(faction => faction != side));
  }

  private void StartNextRound()
  {
    TurnNumber++;
    _sidesActedThisRound.Clear();
    RebuildRoundQueueFromLivingSides();

    if (_turnQueue.Count == 0)
    {
      EndBattle();
      return;
    }

    BeginNextQueuedSideTurn();
  }

  private void BeginNextQueuedSideTurn()
  {
    var nextSide = _turnQueue.Peek();
    ActiveSide = nextSide;
    foreach (var unit in GetFactionAliveUnits(nextSide))
      unit.RefreshForNewTurn();

    RefreshCurrentFactionAvailability();

    RaiseEvents(
      new ActiveSideChangedBattleEvent(nextSide),
      new TurnStartedBattleEvent(nextSide, TurnNumber));
  }

}
