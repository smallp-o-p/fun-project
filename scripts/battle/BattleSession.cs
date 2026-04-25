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
  private Queue<Faction> _turnQueue = [];
  private readonly HashSet<Faction> _sidesActedThisRound = [];
  private readonly HashSet<int> _activeFactionUnitsAvailable = [];
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

  public event Action<BattleEvent> EventRaised;

  public BattleSession(
    BattleBoardState board,
    IEnumerable<Faction> globalFactionOrder,
    IDictionary<Faction, IEnumerable<Combatant>> factionRosters)
  {
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

  internal bool IsUnitStillAvailableThisTurn(int unitId)
  {
    return _activeFactionUnitsAvailable.Contains(unitId);
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
    RaiseEvent(new BattleEvent(BattleEventType.TurnStarted, Message: $"Turn {TurnNumber} started for {ActiveSide.Name}."));
    return true;
  }

  internal BattleUnitState AddUnit(Combatant combatant, Vector3I position, Weapon equippedWeapon = null)
  {
    var unit = new BattleUnitState(_nextUnitId++, combatant, position, equippedWeapon);
    bool occupantSet = Board.TryPlaceOccupant(unit.Position, unit.UnitId);
    if (!occupantSet)
      throw new InvalidOperationException($"Could not place unit {unit.UnitId} at {unit.Position}.");

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

    return unit;
  }

  internal void HandleUnitDeath(BattleUnitState unit)
  {
    var unitSide = unit.Side;
    MoveUnitToDeadStorage(unit);
    bool occupantCleared = Board.TryClearOccupant(unit.Position, unit.UnitId);
    if (!occupantCleared)
      throw new InvalidOperationException($"Could not clear unit {unit.UnitId} from {unit.Position}.");

    _activeFactionUnitsAvailable.Remove(unit.UnitId);
    RaiseEvent(new BattleEvent(BattleEventType.UnitKilled, unit.UnitId, unit.Position, $"Unit ID {unit.UnitId} was killed!"));
    HandleFactionLoss(unitSide);
  }

  internal void RemoveAvailableUnit(int unitId)
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

    RemoveAvailableUnit(unit.UnitId);
    RaiseEvent(new BattleEvent(BattleEventType.UnitActivationEnded, unit.UnitId, unit.Position, $"{unit.Combatant.Name} ended their activation."));

    if (!GetFactionAliveUnits(activeSide).Any(CanUnitActNow))
      EndFactionTurn(activeSide);
  }

  internal void AdvanceTurn()
  {
    if (Phase != BattlePhase.InProgress)
      return;

    var activeSide = ActiveSide;
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

    RaiseEvent(new BattleEvent(BattleEventType.SessionEnded, Message: "Battle ended."));
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

  internal BattleUnitState GetUnitOrNull(int unitId)
  {
    return GetLivingUnitOrNull(unitId) ?? _deadUnits.FirstOrDefault(unit => unit.UnitId == unitId);
  }

  internal BattleUnitState GetLivingUnitOrNull(int unitId)
  {
    return AliveUnits.FirstOrDefault(unit => unit.UnitId == unitId);
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

    RaiseEvent(new BattleEvent(BattleEventType.ActiveSideChanged, Message: $"Active side is now {nextSide.Name}."));
    RaiseEvent(new BattleEvent(BattleEventType.TurnStarted, Message: $"Turn {TurnNumber} started for {nextSide.Name}."));
  }
}
