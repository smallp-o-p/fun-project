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
  internal readonly record struct SpawnedBattleUnit(BattleUnitHandle Handle, BattleUnitState Unit);

  public sealed class BattleUnitHandle
  {
    internal int UnitId { get; }

    internal BattleUnitHandle(int unitId)
    {
      UnitId = unitId;
    }
  }

  public const int DefaultMovementStepActionPointCost = 1;
  private static readonly BattleVisibilitySystem VisibilitySystem = new();

  private readonly Dictionary<Faction, SysColGeneric.HashSet<BattleUnitState>> _aliveUnitsByFaction = [];
  private readonly Dictionary<Faction, IReadOnlyList<Combatant>> _factionRosters = [];
  private readonly Dictionary<BattleUnitHandle, BattleUnitState> _unitsByHandle = [];
  private readonly Dictionary<BattleUnitHandle, BattleBoardState.ValidatedPoint> _unitPositionsByHandle = [];
  private readonly List<BattleUnitState> _deadUnits = [];
  private readonly Queue<Faction> _globalFactionOrder = [];
  private Queue<Faction> _turnQueue = [];
  private readonly SysColGeneric.HashSet<Faction> _sidesActedThisRound = [];
  private readonly SysColGeneric.HashSet<int> _activeFactionUnitsAvailable = [];
  private BattleVisibilitySnapshot _visibilitySnapshot = BattleVisibilitySnapshot.Empty;
  private int _nextUnitId = 1;

  public BattleBoardState Board { get; }
  public BattleQueryRunner Queries { get; }
  public BattlePhase Phase { get; private set; } = BattlePhase.Setup;
  public int TurnNumber { get; private set; } = 1;
  public Faction ActiveSide { get; private set; }
  public IReadOnlyCollection<BattleUnitState> AliveUnits => _aliveUnitsByFaction.Values.SelectMany(units => units).ToArray();
  public IReadOnlyCollection<BattleUnitState> DeadUnits => _deadUnits;
  public IReadOnlyCollection<Faction> GlobalFactionTurnOrder => _globalFactionOrder;
  public IReadOnlyCollection<Faction> TurnQueue => _turnQueue;
  public IReadOnlyDictionary<Faction, IReadOnlyList<Combatant>> FactionRosters => _factionRosters;

  public event Action<BattleEvent> EventRaised = delegate { };

  public BattleSession(
    BattleBoardState board,
    IEnumerable<Faction> globalFactionOrder,
    IDictionary<Faction, IEnumerable<Combatant>> factionRosters)
  {
    ArgumentNullException.ThrowIfNull(board);
    ArgumentNullException.ThrowIfNull(globalFactionOrder);
    ArgumentNullException.ThrowIfNull(factionRosters);

    Board = board;

    Queries = new BattleQueryRunner(this);

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

  internal bool IsUnitStillAvailableThisTurn(BattleUnitHandle handle)
  {
    ArgumentNullException.ThrowIfNull(handle);
    if (!IsHandleFromThisSession(handle))
      return false;

    return _activeFactionUnitsAvailable.Contains(handle.UnitId);
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

    RaiseEvent(new BattleEvent(BattleEventType.SessionStarted, Message: Some("Battle started.")));
    RaiseEvent(new BattleEvent(BattleEventType.TurnStarted, Message: Some($"Turn {TurnNumber} started for {ActiveSide.Name}.")));
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

    var handle = new BattleUnitHandle(_nextUnitId++);
    var unit = new BattleUnitState(handle.UnitId, combatant, equippedWeapon);
    bool occupantSet = Board.TryPlaceOccupant(position, unit.UnitId);
    if (!occupantSet)
      throw new InvalidOperationException($"Could not place unit {unit.UnitId} at {position.Raw}.");

    _unitsByHandle.Add(handle, unit);
    _unitPositionsByHandle.Add(handle, position);

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

    return new SpawnedBattleUnit(handle, unit);
  }

  internal void HandleUnitDeath(BattleUnitState unit)
  {
    var unitSide = unit.Side;
    Option<BattleUnitHandle> handleOption = GetHandleForUnit(unit);
    if (handleOption.IsNone)
      throw new InvalidOperationException($"Could not clear unit {unit.UnitId} because it does not belong to this battle session.");
    BattleUnitHandle handle = handleOption.IfNone(default(BattleUnitHandle));

    Option<BattleBoardState.ValidatedPoint> unitPointOption = GetUnitPosition(handle);
    if (unitPointOption.IsNone)
      throw new InvalidOperationException($"Could not clear unit {unit.UnitId} because it is not on the board.");
    BattleBoardState.ValidatedPoint unitPoint = unitPointOption.IfNone(default(BattleBoardState.ValidatedPoint));
    Vector3I unitPosition = unitPoint.Raw;

    MoveUnitToDeadStorage(unit);

    bool occupantCleared = Board.TryClearOccupant(unitPoint, unit.UnitId);
    if (!occupantCleared)
      throw new InvalidOperationException($"Could not clear unit {unit.UnitId} from {unitPosition}.");
    _unitPositionsByHandle.Remove(handle);

    _activeFactionUnitsAvailable.Remove(unit.UnitId);
    RaiseEvent(new BattleEvent(BattleEventType.UnitKilled, Some(unit.UnitId), Some(unitPosition), Some($"Unit ID {unit.UnitId} was killed!")));
    HandleFactionLoss(unitSide);
  }

  internal void RemoveAvailableUnit(BattleUnitHandle handle)
  {
    ArgumentNullException.ThrowIfNull(handle);
    if (!IsHandleFromThisSession(handle))
      throw new InvalidOperationException("Unit handle does not belong to this battle session.");

    RemoveAvailableUnit(handle.UnitId);
  }

  private void RemoveAvailableUnit(int unitId)
  {
    if (!_activeFactionUnitsAvailable.Remove(unitId))
      throw new InvalidOperationException($"Unit {unitId} is not available this turn.");
  }

  internal void EndUnitActivation(BattleUnitState unit)
  {
    if (Phase != BattlePhase.InProgress)
      throw new InvalidOperationException("Cannot end a unit activation while the battle is not in progress.");

    var activeSide = ActiveSide;
    if (unit.Side != activeSide)
      throw new InvalidOperationException($"Unit {unit.UnitId} is not on the active side.");

    Option<BattleBoardState.ValidatedPoint> unitPointOption = GetUnitPosition(unit);
    if (unitPointOption.IsNone)
      throw new InvalidOperationException($"Cannot end activation for unit {unit.UnitId} because it is not on the board.");
    Vector3I unitPosition = unitPointOption.IfNone(default(BattleBoardState.ValidatedPoint)).Raw;

    RemoveAvailableUnit(unit.UnitId);
    RaiseEvent(new BattleEvent(BattleEventType.UnitActivationEnded, Some(unit.UnitId), Some(unitPosition), Some($"{unit.Combatant.Name} ended their activation.")));

    if (!GetFactionAliveUnits(activeSide).Any(CanUnitActNow))
      EndFactionTurn(activeSide);
  }

  internal void AdvanceTurn()
  {
    if (Phase != BattlePhase.InProgress)
      return;

    var activeSide = ActiveSide;
    RaiseEvent(new BattleEvent(BattleEventType.TurnEnded, Message: Some($"Turn {TurnNumber} ended for {activeSide.Name}.")));
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

    RaiseEvent(new BattleEvent(BattleEventType.SessionEnded, Message: Some("Battle ended.")));
  }

  internal void RegisterSpawnedUnitForCurrentRound(BattleUnitState unit)
  {
    if (Phase != BattlePhase.InProgress)
      throw new InvalidOperationException("Trying to spawn unit while battle hasn't started or is done.");
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

  internal Option<BattleUnitState> GetUnit(BattleUnitHandle handle)
  {
    ArgumentNullException.ThrowIfNull(handle);
    if (!_unitsByHandle.TryGetValue(handle, out var unit))
      return None;

    return Some(unit);
  }

  internal Option<BattleUnitState> GetLivingUnit(BattleUnitHandle handle)
  {
    ArgumentNullException.ThrowIfNull(handle);
    if (!_unitsByHandle.TryGetValue(handle, out var unit))
      return None;
    if (!unit.IsAlive)
      return None;

    return Some(unit);
  }

  internal Option<BattleBoardState.ValidatedPoint> GetUnitPosition(BattleUnitHandle handle)
  {
    ArgumentNullException.ThrowIfNull(handle);
    if (!_unitPositionsByHandle.TryGetValue(handle, out var position))
      return None;

    return Some(position);
  }

  internal Option<BattleBoardState.ValidatedPoint> GetUnitPosition(BattleUnitState unit)
  {
    ArgumentNullException.ThrowIfNull(unit);
    return GetHandleForUnit(unit).Match(
      GetUnitPosition,
      () => None);
  }

  internal bool TryMoveUnit(BattleUnitHandle handle, BattleBoardState.ValidatedPoint source, BattleBoardState.ValidatedPoint destination)
  {
    ArgumentNullException.ThrowIfNull(handle);
    if (!_unitsByHandle.TryGetValue(handle, out var unit))
      return false;
    if (!_unitPositionsByHandle.TryGetValue(handle, out var trackedPosition))
      return false;
    if (trackedPosition != source)
      return false;
    if (!Board.TryMoveOccupant(source, destination, unit.UnitId))
      return false;

    _unitPositionsByHandle[handle] = destination;
    return true;
  }

  private Option<BattleUnitHandle> GetHandleForUnit(BattleUnitState unit)
  {
    ArgumentNullException.ThrowIfNull(unit);
    foreach (var (handle, trackedUnit) in _unitsByHandle)
    {
      if (trackedUnit == unit)
        return Some(handle);
    }

    return None;
  }

  internal void MoveUnitToDeadStorage(BattleUnitState unit)
  {
    if (!_aliveUnitsByFaction.TryGetValue(unit.Side, out var units))
      throw new InvalidOperationException($"Faction {unit.Side.Name} does not have living units to remove.");
    if (!units.Remove(unit))
      throw new InvalidOperationException($"Unit {unit.UnitId} is not tracked as alive.");

    _deadUnits.Add(unit);
  }

  internal bool CanUnitActNow(BattleUnitState unit)
  {
    if (Phase != BattlePhase.InProgress)
      throw new InvalidOperationException("Trying to check unit while the battle is not in progress.");

    if (unit.Side != ActiveSide)
      return false;
    if (!_activeFactionUnitsAvailable.Contains(unit.UnitId))
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

  internal BattleVisibilitySnapshot VisibilitySnapshot => _visibilitySnapshot;

  internal void RefreshCurrentFactionAvailability()
  {
    if (Phase != BattlePhase.InProgress)
      return;

    _activeFactionUnitsAvailable.Clear();
    foreach (var unit in GetFactionAliveUnits(ActiveSide))
      _activeFactionUnitsAvailable.Add(unit.UnitId);
  }

  internal void RaiseEvent(BattleEvent battleEvent)
  {
    EventRaised.Invoke(battleEvent);
  }

  internal void RefreshVisibility()
  {
    _visibilitySnapshot = VisibilitySystem
      .Build(this)
      .WithMergedExplored(_visibilitySnapshot);
  }

  internal bool IsHandleFromThisSession(BattleUnitHandle handle)
  {
    ArgumentNullException.ThrowIfNull(handle);
    return _unitsByHandle.ContainsKey(handle);
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

    RaiseEvent(new BattleEvent(BattleEventType.ActiveSideChanged, Message: Some($"Active side is now {nextSide.Name}.")));
    RaiseEvent(new BattleEvent(BattleEventType.TurnStarted, Message: Some($"Turn {TurnNumber} started for {nextSide.Name}.")));
  }
}
