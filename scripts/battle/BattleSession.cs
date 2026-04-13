#nullable enable
using FunProject.Combatants;
using FunProject.Weapons;
using Godot;
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
  public const int DefaultMovementStepActionPointCost = 1;
  private static readonly BattleVisibilitySystem VisibilitySystem = new();

  private readonly Dictionary<Faction, HashSet<BattleUnitState>> _aliveUnitsByFaction = [];
  private readonly Dictionary<Faction, IReadOnlyList<Combatant>> _factionRosters = [];
  private readonly List<BattleUnitState> _deadUnits = [];
  private readonly Queue<Faction> _globalFactionOrder = [];
  private readonly Queue<Faction> _turnQueue = [];
  private readonly HashSet<Faction> _sidesActedThisRound = [];
  private readonly HashSet<int> _activeFactionUnitsAvailable = [];
  private BattleVisibilitySnapshot _visibilitySnapshot = BattleVisibilitySnapshot.Empty;
  private int _nextUnitId = 1;

  public BattleBoardState Board { get; }
  public BattlePhase Phase { get; private set; } = BattlePhase.Setup;
  public int TurnNumber { get; private set; } = 1;
  public Faction? ActiveSide { get; private set; }
  public IReadOnlyCollection<BattleUnitState> AliveUnits => _aliveUnitsByFaction.Values.SelectMany(units => units).ToArray();
  public IReadOnlyCollection<BattleUnitState> DeadUnits => _deadUnits;
  public IReadOnlyCollection<Faction> GlobalFactionTurnOrder => _globalFactionOrder;
  public IReadOnlyCollection<Faction> TurnQueue => _turnQueue;
  public IReadOnlyDictionary<Faction, IReadOnlyList<Combatant>> FactionRosters => _factionRosters;

  public event Action<BattleEvent>? EventRaised;

  public BattleSession(
    Vector3I dimensions,
    IEnumerable<Faction> globalFactionOrder,
    IDictionary<Faction, IEnumerable<Combatant>> factionRosters)
  {
    if (dimensions.X <= 0 || dimensions.Y <= 0 || dimensions.Z <= 0)
      throw new ArgumentOutOfRangeException(nameof(dimensions));

    ArgumentNullException.ThrowIfNull(globalFactionOrder);
    ArgumentNullException.ThrowIfNull(factionRosters);

    Board = new BattleBoardState(dimensions);

    foreach (var faction in globalFactionOrder)
    {
      ArgumentNullException.ThrowIfNull(faction);
      EnqueueFactionInGlobalOrder(faction);
    }

    foreach (var (faction, roster) in factionRosters)
    {
      ArgumentNullException.ThrowIfNull(faction);
      ArgumentNullException.ThrowIfNull(roster);

      List<Combatant> copiedRoster = [];
      foreach (var combatant in roster)
      {
        ArgumentNullException.ThrowIfNull(combatant);
        copiedRoster.Add(combatant);
      }

      _factionRosters[faction] = copiedRoster;
      EnqueueFactionInGlobalOrder(faction);
    }
  }

  public IEnumerable<BattleUnitState> GetFactionAlive(Faction side)
  {
    ArgumentNullException.ThrowIfNull(side);
    if (_aliveUnitsByFaction.TryGetValue(side, out var units))
      return units;

    return Array.Empty<BattleUnitState>();
  }

  public IEnumerable<BattleUnitState> GetFactionDead(Faction side)
  {
    ArgumentNullException.ThrowIfNull(side);
    return _deadUnits.Where(unit => unit.Side == side);
  }

  public bool IsUnitStillAvailableThisTurn(int unitId)
  {
    return _activeFactionUnitsAvailable.Contains(unitId);
  }

  public bool CanUnitActNow(int unitId)
  {
    var unit = GetLivingUnitOrNull(unitId);
    return unit != null && CanUnitActNow(unit);
  }

  public bool IsUnitVisibleToUnit(int observerUnitId, int targetUnitId)
  {
    var observer = GetLivingUnitOrNull(observerUnitId);
    var target = GetLivingUnitOrNull(targetUnitId);
    if (observer == null || target == null)
      return false;
    if (observer.UnitId == target.UnitId)
      return true;

    return _visibilitySnapshot
      .GetVisibleUnitsForObserverOrEmpty(observerUnitId)
      .Contains(targetUnitId);
  }

  public bool IsUnitVisibleToFaction(Faction faction, int targetUnitId)
  {
    ArgumentNullException.ThrowIfNull(faction);

    var target = GetLivingUnitOrNull(targetUnitId);
    if (target == null)
      return false;
    if (target.Side == faction)
      return true;

    return _visibilitySnapshot
      .GetFactionStateOrEmpty(faction)
      .VisibleForeignUnitIds
      .Contains(targetUnitId);
  }

  public bool IsTileVisibleToFaction(Faction faction, Vector3I tile)
  {
    ArgumentNullException.ThrowIfNull(faction);
    if (!Board.IsInBounds(tile))
      return false;

    return _visibilitySnapshot
      .GetFactionStateOrEmpty(faction)
      .VisibleTiles
      .Contains(tile);
  }

  public bool HasFactionExploredTile(Faction faction, Vector3I tile)
  {
    ArgumentNullException.ThrowIfNull(faction);
    if (!Board.IsInBounds(tile))
      return false;

    return _visibilitySnapshot
      .GetFactionStateOrEmpty(faction)
      .ExploredTiles
      .Contains(tile);
  }

  public IEnumerable<BattleUnitState> GetVisibleUnitsForFaction(Faction faction)
  {
    ArgumentNullException.ThrowIfNull(faction);

    var visibleForeignUnitIds = _visibilitySnapshot
      .GetFactionStateOrEmpty(faction)
      .VisibleForeignUnitIds;

    return AliveUnits.Where(unit => unit.Side == faction || visibleForeignUnitIds.Contains(unit.UnitId));
  }

  public IEnumerable<Vector3I> GetVisibleTilesForFaction(Faction faction)
  {
    ArgumentNullException.ThrowIfNull(faction);
    return _visibilitySnapshot.GetFactionStateOrEmpty(faction).VisibleTiles;
  }

  public IEnumerable<Vector3I> GetExploredTilesForFaction(Faction faction)
  {
    ArgumentNullException.ThrowIfNull(faction);
    return _visibilitySnapshot.GetFactionStateOrEmpty(faction).ExploredTiles;
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

    RaiseEvent(new BattleEvent(BattleEventType.SessionStarted, Message: "Battle started."));
    RaiseEvent(new BattleEvent(BattleEventType.TurnStarted, Message: $"Turn {TurnNumber} started for {ActiveSide?.Name}."));
    return true;
  }

  internal BattleUnitState CreateUnitState(Combatant combatant, Vector3I position, Weapon? equippedWeapon = null)
  {
    ArgumentNullException.ThrowIfNull(combatant);
    return new BattleUnitState(_nextUnitId++, combatant, position, equippedWeapon);
  }

  internal bool TryAddUnit(BattleUnitState unit)
  {
    ArgumentNullException.ThrowIfNull(unit);

    if (!_aliveUnitsByFaction.TryGetValue(unit.Side, out var unitsForSide))
    {
      unitsForSide = [];
      _aliveUnitsByFaction.Add(unit.Side, unitsForSide);
    }

    unitsForSide.Add(unit);
    EnqueueFactionInGlobalOrder(unit.Side);

    var occupantSet = Board.TrySetOccupant(unit.Position, unit.UnitId);
    if (!occupantSet)
    {
      unitsForSide.Remove(unit);
      if (unitsForSide.Count == 0)
        _aliveUnitsByFaction.Remove(unit.Side);

      return false;
    }

    RegisterSpawnedUnitForCurrentRound(unit);
    return true;
  }

  internal bool TryMoveUnit(BattleUnitState unit, Vector3I destination)
  {
    ArgumentNullException.ThrowIfNull(unit);

    var sourceTile = Board.GetTileOrNull(unit.Position);
    var destinationTile = Board.GetTileOrNull(destination);
    if (sourceTile == null || destinationTile == null)
      return false;
    if (!destinationTile.TrySetOccupant(unit.UnitId))
      return false;

    sourceTile.ClearOccupant();
    unit.MoveTo(destination);
    return true;
  }

  internal void HandleUnitDeath(BattleUnitState unit)
  {
    ArgumentNullException.ThrowIfNull(unit);

    var unitSide = unit.Side;
    MoveUnitToDeadStorage(unit);
    ClearTileOccupant(unit.Position);

    _activeFactionUnitsAvailable.Remove(unit.UnitId);
    RaiseEvent(new BattleEvent(BattleEventType.UnitKilled, unit.UnitId, unit.Position, $"Unit ID {unit.UnitId} was killed!"));
    HandleFactionLoss(unitSide);
  }

  internal void ClearTileOccupant(Vector3I coordinates)
  {
    var tile = Board.GetTileOrNull(coordinates);
    tile?.ClearOccupant();
  }

  internal bool TryRemoveAvailableUnit(int unitId)
  {
    return _activeFactionUnitsAvailable.Remove(unitId);
  }

  internal void AdvanceTurn()
  {
    if (Phase != BattlePhase.InProgress)
      return;

    var activeSide = ActiveSide;
    if (activeSide == null)
      return;

    RaiseEvent(new BattleEvent(BattleEventType.TurnEnded, Message: $"Turn {TurnNumber} ended for {activeSide.Name}."));
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

  internal bool TryEndFactionTurn(Faction expectedActiveSide)
  {
    ArgumentNullException.ThrowIfNull(expectedActiveSide);

    if (Phase != BattlePhase.InProgress || ActiveSide == null || ActiveSide != expectedActiveSide)
      return false;

    AdvanceTurn();
    return true;
  }

  internal void EndBattle()
  {
    if (Phase == BattlePhase.Ended)
      return;

    ActiveSide = null;
    _activeFactionUnitsAvailable.Clear();
    _turnQueue.Clear();
    Phase = BattlePhase.Ended;

    RaiseEvent(new BattleEvent(BattleEventType.SessionEnded, Message: "Battle ended."));
  }

  internal void RegisterSpawnedUnitForCurrentRound(BattleUnitState unit)
  {
    ArgumentNullException.ThrowIfNull(unit);

    if (Phase != BattlePhase.InProgress)
      return;
    if (ActiveSide == null)
      return;

    if (unit.Side == ActiveSide)
    {
      _activeFactionUnitsAvailable.Add(unit.UnitId);
      return;
    }

    if (_sidesActedThisRound.Contains(unit.Side))
      return;
    if (_turnQueue.Contains(unit.Side))
      return;

    _turnQueue.Enqueue(unit.Side);
  }

  internal BattleUnitState? GetUnitOrNull(int unitId)
  {
    var livingUnit = GetLivingUnitOrNull(unitId);
    if (livingUnit != null)
      return livingUnit;

    return _deadUnits.FirstOrDefault(unit => unit.UnitId == unitId);
  }

  internal BattleUnitState? GetLivingUnitOrNull(int unitId)
  {
    return AliveUnits.FirstOrDefault(unit => unit.UnitId == unitId);
  }

  internal void MoveUnitToDeadStorage(BattleUnitState unit)
  {
    ArgumentNullException.ThrowIfNull(unit);

    if (!_aliveUnitsByFaction.TryGetValue(unit.Side, out var units))
      return;
    if (!units.Remove(unit))
      return;
    if (units.Count == 0)
      _aliveUnitsByFaction.Remove(unit.Side);

    _deadUnits.Add(unit);
  }

  internal bool CanUnitActNow(BattleUnitState unit)
  {
    ArgumentNullException.ThrowIfNull(unit);

    if (ActiveSide == null)
      return false;
    if (unit.Side != ActiveSide)
      return false;
    if (!_activeFactionUnitsAvailable.Contains(unit.UnitId))
      return false;

    return unit.CurrentActionPoints > 0;
  }

  internal static bool IsAdjacent(Vector3I source, Vector3I destination)
  {
    var delta = source - destination;
    return Mathf.Abs(delta.X) + Mathf.Abs(delta.Y) + Mathf.Abs(delta.Z) == 1;
  }

  internal static int GetGridDistance(Vector3I source, Vector3I destination)
  {
    var delta = source - destination;
    return Mathf.Abs(delta.X) + Mathf.Abs(delta.Y) + Mathf.Abs(delta.Z);
  }

  internal bool HasLivingUnits(Faction side)
  {
    ArgumentNullException.ThrowIfNull(side);
    return _aliveUnitsByFaction.TryGetValue(side, out var units) && units.Count > 0;
  }

  internal void RefreshCurrentFactionAvailability()
  {
    if (ActiveSide == null)
      return;
    _activeFactionUnitsAvailable.Clear();
    foreach (var unit in GetFactionAlive(ActiveSide))
      _activeFactionUnitsAvailable.Add(unit.UnitId);
  }

  internal void RaiseEvent(BattleEvent battleEvent)
  {
    EventRaised?.Invoke(battleEvent);
  }

  internal void RefreshVisibility()
  {
    _visibilitySnapshot = VisibilitySystem
      .Build(this)
      .WithMergedExplored(_visibilitySnapshot);
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
    ArgumentNullException.ThrowIfNull(side);
    if (_globalFactionOrder.Contains(side))
      return;

    _globalFactionOrder.Enqueue(side);
  }

  private void HandleFactionLoss(Faction side)
  {
    ArgumentNullException.ThrowIfNull(side);

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
    ArgumentNullException.ThrowIfNull(side);

    if (!_turnQueue.Contains(side))
      return;

    var existingOrder = _turnQueue.ToArray();
    _turnQueue.Clear();

    foreach (var queuedSide in existingOrder)
    {
      if (queuedSide != side && !_turnQueue.Contains(queuedSide))
        _turnQueue.Enqueue(queuedSide);
    }
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
    foreach (var unit in GetFactionAlive(nextSide))
      unit.RefreshForNewTurn();

    RefreshCurrentFactionAvailability();

    RaiseEvent(new BattleEvent(BattleEventType.ActiveSideChanged, Message: $"Active side is now {nextSide.Name}."));
    RaiseEvent(new BattleEvent(BattleEventType.TurnStarted, Message: $"Turn {TurnNumber} started for {nextSide.Name}."));
  }
}
