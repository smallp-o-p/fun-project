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
  private static readonly BattleVisibilitySystem VisibilitySystem = new();

  private readonly Dictionary<Faction, SysColGeneric.HashSet<BattleUnitState>> _aliveUnitsByFaction = [];
  private readonly Dictionary<Faction, IReadOnlyList<Combatant>> _factionRosters = [];
  private readonly List<BattleUnitState> _units = [];
  private readonly Dictionary<BattleUnitState, BattleBoardState.ValidatedPoint> _unitToPosition = [];
  private readonly Queue<Faction> _globalFactionOrder = [];
  private Queue<Faction> _turnQueue = [];
  private readonly SysColGeneric.HashSet<Faction> _sidesActedThisRound = [];
  private readonly SysColGeneric.HashSet<BattleUnitState> _activeFactionUnitsAvailable = [];
  private BattleVisibilitySnapshot _visibilitySnapshot = BattleVisibilitySnapshot.Empty;

  public BattleBoardState Board { get; }
  public BattlePhase Phase { get; private set; } = BattlePhase.Setup;
  public int TurnNumber { get; private set; } = 1;
  public Faction ActiveSide { get; private set; }
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

    RaiseCommittedEvent(new SessionStartedBattleEvent());
    RaiseCommittedEvent(new TurnStartedBattleEvent(
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
    _unitToPosition.Add(unit, position);

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

    RaiseCommittedEvent(new UnitAddedBattleEvent(unit, position));

    return new SpawnedBattleUnit(unit);
  }

  public void DealDamageTo(BattleUnitState unit, int dmg)
  {
    unit.ReceiveDamage(dmg);
    RaiseCommittedEvent(new UnitDamagedBattleEvent(unit, dmg));
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

    if (!_aliveUnitsByFaction.TryGetValue(unit.Side, out var units))
      throw new InvalidOperationException($"Faction {unit.Side.Name} does not have living units to remove.");
    if (!units.Remove(unit))
      throw new InvalidOperationException($"Unit {unit.Id} is not tracked as alive.");

    _activeFactionUnitsAvailable.Remove(unit);
    RaiseCommittedEvent(new UnitKilledBattleEvent(unit, unitPoint));
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
    RaiseCommittedEvent(new UnitActivationEndedBattleEvent(unit, unitPoint));

    if (!GetFactionAliveUnits(activeSide).Any(CanUnitActNow))
      EndFactionTurn(activeSide);
  }

  internal void AdvanceTurn()
  {
    if (Phase != BattlePhase.InProgress)
      return;

    var activeSide = ActiveSide;
    RaiseCommittedEvent(new TurnEndedBattleEvent(
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

    RaiseCommittedEvent(new SessionEndedBattleEvent());
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

    if (!_unitToPosition.TryGetValue(unit, out var position))
      return None;

    return Some(position);
  }

  internal Option<BattleUnitState> GetUnitAt(BattleBoardState.ValidatedPoint point)
  {
    BattleTileState tile = Board.GetTile(point);

    return tile.OccupantUnitId.Match(
      unitId =>
      {
        if (unitId < 0 || unitId >= _units.Count)
          return None;

        return Some(_units[unitId]);
      },
      () => None);
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
    if (!_unitToPosition.TryGetValue(unit, out var trackedPosition))
      return false;
    if (trackedPosition != source)
      return false;
    if (!Board.TryMoveOccupant(source, destination, unit.Id))
      return false;

    _unitToPosition[unit] = destination;

    RaiseCommittedEvent(new UnitMovedBattleEvent(unit, destination, source));
    RaiseCommittedEvent(new TileOccupiedBattleEvent(unit, destination));

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

  internal BattleVisibilitySnapshot VisibilitySnapshot => _visibilitySnapshot;

  internal void RefreshCurrentFactionAvailability()
  {
    if (Phase != BattlePhase.InProgress)
      return;

    _activeFactionUnitsAvailable.Clear();
    foreach (var unit in GetFactionAliveUnits(ActiveSide))
      _activeFactionUnitsAvailable.Add(unit);
  }

  internal void RaiseCommittedEvent(BattleEvent battleEvent)
  {
    RefreshVisibility();
    BattleEventCommitted.Invoke(battleEvent);
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

    RaiseCommittedEvent(new ActiveSideChangedBattleEvent(nextSide));
    RaiseCommittedEvent(new TurnStartedBattleEvent(
      nextSide,
      TurnNumber));
  }
}
