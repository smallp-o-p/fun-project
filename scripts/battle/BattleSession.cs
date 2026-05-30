using FunProject.Combatants;
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

  private readonly Dictionary<Faction, SysColGeneric.HashSet<BattleUnitState>> _aliveUnitsByFaction = [];
  private readonly Dictionary<Faction, IReadOnlyList<Combatant>> _factionRosters = [];
  private readonly List<BattleUnitState> _units = [];
  private readonly UnitPositionIndex _unitPositions = new();
  private readonly Dictionary<Faction, SysColGeneric.HashSet<BattleBoardState.ValidatedPoint>> _exploredTilesByFaction = [];
  private readonly Queue<Faction> _globalFactionOrder = [];
  private Queue<Faction> _turnQueue = [];
  private readonly SysColGeneric.HashSet<Faction> _sidesActedThisRound = [];
  private readonly SysColGeneric.HashSet<BattleUnitState> _activeFactionUnitsAvailable = [];

  public BattleBoardState Board { get; }
  public BattlePhase Phase { get; private set; } = BattlePhase.Setup;
  public int TurnNumber { get; private set; } = 1;
  public Faction ActiveSide { get; private set; }
  internal IEnumerable<BattleUnitState> Units => _units;
  public IEnumerable<BattleUnitState> AliveUnits => _units.Where((unit) => unit.IsAlive);
  public IEnumerable<BattleUnitState> DeadUnits => _units.Where((unit) => unit.IsDead);
  public IReadOnlyCollection<Faction> GlobalFactionTurnOrder => _globalFactionOrder;
  public IReadOnlyCollection<Faction> TurnQueue => _turnQueue;
  public IReadOnlyDictionary<Faction, IReadOnlyList<Combatant>> FactionRosters => _factionRosters;

  public event Action<BattleEvent> BattleEventCommitted = delegate { };

  public BattleSession(
    BattleBoardState board,
    IEnumerable<Faction> globalFactionOrder,
    IDictionary<Faction, IEnumerable<Combatant>> factionRosters)
  {
    ArgumentNullException.ThrowIfNull(board);
    ArgumentNullException.ThrowIfNull(globalFactionOrder);
    ArgumentNullException.ThrowIfNull(factionRosters);

    Board = board;

    foreach (var faction in globalFactionOrder)
    {
      EnqueueFactionInGlobalOrder(faction);
    }

    foreach (var (faction, roster) in factionRosters)
    {
      List<Combatant> copiedRoster = [];
      foreach (var combatant in roster)
      {
        copiedRoster.Add(combatant);
      }

      _factionRosters[faction] = copiedRoster;
      EnqueueFactionInGlobalOrder(faction);
    }

    if (_globalFactionOrder.Count == 0)
      throw new ArgumentException("Battle session requires at least one faction in the global order or faction rosters.");

    _turnQueue = new Queue<Faction>(_globalFactionOrder);
    ActiveSide = _turnQueue.Peek();
  }

  internal IEnumerable<BattleUnitState> GetFactionAliveUnits(Faction side)
  {
    return _aliveUnitsByFaction.TryGetValue(side, out var units) ? units : [];
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

  internal bool IsUnitStillAvailableThisTurn(BattleUnitState unit)
  {
    ArgumentNullException.ThrowIfNull(unit);
    return unit.BelongsTo(this) && _activeFactionUnitsAvailable.Contains(unit);
  }

  internal bool TryStartBattle()
  {
    if (Phase != BattlePhase.Setup)
      return false;

    RebuildRoundQueueFromLivingSides();
    if (_turnQueue.Count == 0)
      return false;

    Phase = BattlePhase.InProgress;
    TurnNumber = 1;
    ActiveSide = _turnQueue.Peek();
    _sidesActedThisRound.Clear();

    foreach (var unit in AliveUnits)
      unit.RefreshForNewTurn();

    RefreshCurrentFactionAvailability();

    RaiseEvent(new SessionStartedBattleEvent());
    RaiseEvent(new TurnStartedBattleEvent(
      ActiveSide,
      TurnNumber));
    return true;
  }

  internal SpawnedBattleUnit AddUnit(Combatant combatant, Vector3I position)
  {
    return AddUnit(combatant, position, None);
  }

  internal SpawnedBattleUnit AddUnit(Combatant combatant, BattleBoardState.ValidatedPoint position)
  {
    return AddUnit(combatant, position, None);
  }

  internal SpawnedBattleUnit AddUnit(Combatant combatant, Vector3I position, Option<Weapon> equippedWeapon)
  {
    Option<BattleBoardState.ValidatedPoint> positionPointOption = Board.ValidatePoint(position);
    if (positionPointOption.IsNone)
      throw new InvalidOperationException($"Could not place unit at invalid board position {position}.");
    BattleBoardState.ValidatedPoint positionPoint = positionPointOption.IfNone(default(BattleBoardState.ValidatedPoint));

    return AddUnit(combatant, positionPoint, equippedWeapon);
  }

  internal SpawnedBattleUnit AddUnit(Combatant combatant, BattleBoardState.ValidatedPoint position, Option<Weapon> equippedWeapon)
  {
    ArgumentNullException.ThrowIfNull(combatant);

    var unit = new BattleUnitState(this, _units.Count, combatant, equippedWeapon);
    bool occupantSet = Board.TryPlaceOccupant(position, unit.Id);
    if (!occupantSet)
      throw new InvalidOperationException($"Could not place unit {unit.Id} at {position.Raw}.");

    _units.Add(unit);
    _unitPositions.Add(unit, position);

    if (!_aliveUnitsByFaction.TryGetValue(unit.Side, out var unitsForSide))
    {
      unitsForSide = [];
      _aliveUnitsByFaction.Add(unit.Side, unitsForSide);
    }

    unitsForSide.Add(unit);
    EnqueueFactionInGlobalOrder(unit.Side);

    if (Phase == BattlePhase.Setup && !_turnQueue.Contains(unit.Side))
      _turnQueue.Enqueue(unit.Side);
    if (Phase == BattlePhase.InProgress)
      RegisterSpawnedUnitForCurrentRound(unit);

    RaiseEvent(new UnitAddedBattleEvent(unit, position));

    return new SpawnedBattleUnit(unit);
  }

  public void DealDamageTo(BattleUnitState unit, int dmg)
  {
    unit.ReceiveDamage(dmg);
    RaiseEvent(new UnitDamagedBattleEvent(unit, dmg));
    if (unit.IsDead)
    {
      HandleUnitDeath(unit);
    }
  }

  internal void HandleUnitDeath(BattleUnitState unit)
  {
    ArgumentNullException.ThrowIfNull(unit);

    var unitSide = unit.Side;
    Option<BattleBoardState.ValidatedPoint> unitPointOption = GetUnitPosition(unit);
    if (unitPointOption.IsNone)
      throw new InvalidOperationException($"Could not clear unit {unit.Id} because it is not on the board.");
    BattleBoardState.ValidatedPoint unitPoint = unitPointOption.Value();

    bool occupantCleared = Board.TryClearOccupant(unitPoint, unit.Id);
    if (!occupantCleared)
      throw new InvalidOperationException($"Could not clear unit {unit.Id} from {unitPoint.Raw}.");
    if (!_unitPositions.Remove(unit))
      throw new InvalidOperationException($"Could not remove unit {unit.Id} from the session position index.");

    if (!_aliveUnitsByFaction.TryGetValue(unit.Side, out var units))
      throw new InvalidOperationException($"Faction {unit.Side.Name} does not have living units to remove.");
    if (!units.Remove(unit))
      throw new InvalidOperationException($"Unit {unit.Id} is not tracked as alive.");

    _activeFactionUnitsAvailable.Remove(unit);
    RaiseEvent(new UnitKilledBattleEvent(unit, unitPoint));
    HandleFactionLoss(unitSide);
  }

  internal void RemoveAvailableUnit(BattleUnitState unit)
  {
    ArgumentNullException.ThrowIfNull(unit);
    if (!unit.BelongsTo(this))
      throw new InvalidOperationException("Unit does not belong to this battle session.");

    if (!_activeFactionUnitsAvailable.Remove(unit))
      throw new InvalidOperationException($"Unit {unit.Id} is not available this turn.");
  }

  internal void EndUnitActivation(BattleUnitState unit)
  {
    if (Phase != BattlePhase.InProgress)
      throw new InvalidOperationException("Cannot end a unit activation while the battle is not in progress.");

    var activeSide = ActiveSide;
    if (unit.Side != activeSide)
      throw new InvalidOperationException($"Unit {unit.Id} is not on the active side.");

    Option<BattleBoardState.ValidatedPoint> unitPointOption = GetUnitPosition(unit);
    if (unitPointOption.IsNone)
      throw new InvalidOperationException($"Cannot end activation for unit {unit.Id} because it is not on the board.");
    BattleBoardState.ValidatedPoint unitPoint = unitPointOption.IfNone(default(BattleBoardState.ValidatedPoint));

    RemoveAvailableUnit(unit);
    RaiseEvent(new UnitActivationEndedBattleEvent(unit, unitPoint));

    if (!GetFactionAliveUnits(activeSide).Any(CanUnitActNow))
      EndFactionTurn(activeSide);
  }

  internal void AdvanceTurn()
  {
    if (Phase != BattlePhase.InProgress)
      return;

    var activeSide = ActiveSide;
    RaiseEvent(new TurnEndedBattleEvent(
      activeSide,
      TurnNumber));
    _sidesActedThisRound.Add(activeSide);

    if (_turnQueue.Count > 0)
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

  internal void EndBattle()
  {
    if (Phase == BattlePhase.Ended)
      return;

    _activeFactionUnitsAvailable.Clear();
    _turnQueue.Clear();
    Phase = BattlePhase.Ended;

    RaiseEvent(new SessionEndedBattleEvent());
  }

  internal void RegisterSpawnedUnitForCurrentRound(BattleUnitState unit)
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
    if (!unit.BelongsTo(this))
      throw new InvalidOperationException($"Unit {unit.Id} does not belong to this battle session.");

    if (!_unitPositions.TryGetUnitPosition(unit, out var position))
      return None;

    return Some(position);
  }

  internal Option<BattleUnitState> GetUnitAt(BattleBoardState.ValidatedPoint point)
  {
    return _unitPositions.TryGetUnitAt(point, out var unit) ? Some(unit) : None;
  }

  internal Option<BattleUnitState> GetUnitAt(Vector3I position)
  {
    return Board.ValidatePoint(position).Match(
      GetUnitAt,
      () => None);
  }

  internal bool TryMoveUnit(BattleUnitState unit, BattleBoardState.ValidatedPoint source, BattleBoardState.ValidatedPoint destination)
  {
    ArgumentNullException.ThrowIfNull(unit);
    if (!unit.BelongsTo(this))
      return false;
    if (!_unitPositions.TryGetUnitPosition(unit, out var trackedPosition))
      return false;
    if (trackedPosition != source)
      return false;
    if (!Board.TryMoveOccupant(source, destination, unit.Id))
      return false;

    _unitPositions.Move(unit, source, destination);

    RaiseEvent(new UnitMovedBattleEvent(unit, destination, source));
    RaiseEvent(new TileOccupiedBattleEvent(unit, destination));

    return true;
  }

  internal bool CanUnitActNow(BattleUnitState unit)
  {
    if (Phase != BattlePhase.InProgress)
      throw new InvalidOperationException("Trying to check unit while the battle is not in progress.");

    if (unit.Side != ActiveSide)
      return false;
    if (!_activeFactionUnitsAvailable.Contains(unit))
      return false;

    return unit.CurrentActionPoints > 0;
  }

  internal static int GetGridDistance(Vector3I source, Vector3I destination)
  {
    var delta = source - destination;
    return Mathf.Abs(delta.X) + Mathf.Abs(delta.Y) + Mathf.Abs(delta.Z);
  }

  internal bool HasLivingUnits(Faction side)
  {
    return _aliveUnitsByFaction.TryGetValue(side, out var units) && units.Count > 0;
  }

  internal void RefreshCurrentFactionAvailability()
  {
    if (Phase != BattlePhase.InProgress)
      return;

    _activeFactionUnitsAvailable.Clear();
    foreach (var unit in GetFactionAliveUnits(ActiveSide))
      _activeFactionUnitsAvailable.Add(unit);
  }

  internal void RaiseEvent(BattleEvent battleEvent)
  {
    RefreshVisibility();
    BattleEventCommitted.Invoke(battleEvent);
  }

  internal void RefreshVisibility()
  {
    foreach (var unit in Units)
      unit.ClearVisibility();

    foreach (var observer in AliveUnits)
    {
      if (!_unitPositions.TryGetUnitPosition(observer, out var observerPosition))
        throw new InvalidOperationException($"Living unit {observer.Id} is missing from the session position index.");

      SysColGeneric.HashSet<BattleBoardState.ValidatedPoint> observerVisibleTiles = GetVisibleTiles(observer, observerPosition);
      foreach (var visibleTile in observerVisibleTiles)
        observer.AddVisibleTile(visibleTile);

      MarkTilesExplored(observer.Side, observerVisibleTiles);

      foreach (var visibleTile in observerVisibleTiles)
      {
        if (!_unitPositions.TryGetUnitAt(visibleTile, out var target))
          continue;
        if (ReferenceEquals(target, observer) || target.IsDead)
          continue;

        observer.AddVisibleUnit(target);
      }
    }
  }

  internal void MarkTilesExplored(Faction side, IEnumerable<BattleBoardState.ValidatedPoint> tiles)
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
    if (_turnQueue.Count == 0)
      return;

    var existingOrder = _turnQueue.ToArray();
    _turnQueue.Clear();

    foreach (var side in existingOrder)
    {
      if (HasLivingUnits(side) && !_turnQueue.Contains(side))
        _turnQueue.Enqueue(side);
    }
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
    if (Phase != BattlePhase.InProgress)
    {
      RemoveSideFromQueue(side);
      return;
    }

    if (ActiveSide != side)
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
    if (_aliveUnitsByFaction.Count == 0)
    {
      EndBattle();
      return;
    }

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
    if (_turnQueue.Count == 0)
      return;

    var nextSide = _turnQueue.Peek();
    ActiveSide = nextSide;
    foreach (var unit in GetFactionAliveUnits(nextSide))
      unit.RefreshForNewTurn();

    RefreshCurrentFactionAvailability();

    RaiseEvent(new ActiveSideChangedBattleEvent(nextSide));
    RaiseEvent(new TurnStartedBattleEvent(
      nextSide,
      TurnNumber));
  }

  private sealed class UnitPositionIndex
  {
    private readonly Dictionary<BattleUnitState, BattleBoardState.ValidatedPoint> _positionByUnit = [];
    private readonly Dictionary<BattleBoardState.ValidatedPoint, BattleUnitState> _unitByPosition = [];

    internal void Add(BattleUnitState unit, BattleBoardState.ValidatedPoint position)
    {
      ArgumentNullException.ThrowIfNull(unit);
      if (_positionByUnit.ContainsKey(unit))
        throw new InvalidOperationException($"Unit {unit.Id} is already in the session position index.");
      if (_unitByPosition.ContainsKey(position))
        throw new InvalidOperationException($"Position {position.Raw} is already occupied in the session position index.");

      _positionByUnit.Add(unit, position);
      _unitByPosition.Add(position, unit);
    }

    internal bool TryGetUnitPosition(BattleUnitState unit, out BattleBoardState.ValidatedPoint position)
    {
      ArgumentNullException.ThrowIfNull(unit);
      return _positionByUnit.TryGetValue(unit, out position);
    }

    internal bool TryGetUnitAt(BattleBoardState.ValidatedPoint position, out BattleUnitState unit)
    {
      return _unitByPosition.TryGetValue(position, out unit!);
    }

    internal void Move(
      BattleUnitState unit,
      BattleBoardState.ValidatedPoint source,
      BattleBoardState.ValidatedPoint destination)
    {
      ArgumentNullException.ThrowIfNull(unit);
      if (!_positionByUnit.TryGetValue(unit, out var indexedSource) || indexedSource != source)
        throw new InvalidOperationException($"Unit {unit.Id} is not indexed at source position {source.Raw}.");
      if (!_unitByPosition.TryGetValue(source, out var indexedUnit) || !ReferenceEquals(indexedUnit, unit))
        throw new InvalidOperationException($"Source position {source.Raw} is not indexed to unit {unit.Id}.");
      if (source == destination)
        return;
      if (_unitByPosition.ContainsKey(destination))
        throw new InvalidOperationException($"Destination position {destination.Raw} is already occupied in the session position index.");

      _unitByPosition.Remove(source);
      _unitByPosition.Add(destination, unit);
      _positionByUnit[unit] = destination;
    }

    internal bool Remove(BattleUnitState unit)
    {
      ArgumentNullException.ThrowIfNull(unit);
      if (!_positionByUnit.TryGetValue(unit, out var position))
        return false;
      if (!_unitByPosition.TryGetValue(position, out var indexedUnit) || !ReferenceEquals(indexedUnit, unit))
        throw new InvalidOperationException($"Position {position.Raw} is not indexed to unit {unit.Id}.");

      _positionByUnit.Remove(unit);
      return _unitByPosition.Remove(position);
    }
  }
}
