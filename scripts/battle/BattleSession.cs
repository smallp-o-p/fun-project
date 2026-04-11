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

  private readonly Dictionary<int, BattleUnitState> _units = [];
  private readonly Queue<Faction> _turnQueue = [];
  private readonly HashSet<Faction> _queuedSides = [];
  private readonly HashSet<Faction> _sidesActedThisRound = [];
  private int _nextUnitId = 1;

  public BattleBoardState Board { get; }
  public BattlePhase Phase { get; private set; } = BattlePhase.Setup;
  public int TurnNumber { get; private set; }
  public Faction? ActiveSide { get; private set; }
  public int? SelectedUnitId { get; private set; }
  public IReadOnlyCollection<BattleUnitState> Units => _units.Values;
  public IReadOnlyCollection<Faction> TurnQueue => _turnQueue.ToArray();
  public BattleUnitState? SelectedUnit => SelectedUnitId.HasValue && _units.TryGetValue(SelectedUnitId.Value, out var unit) ? unit : null;

  public event Action<BattleEvent>? EventRaised;

  public BattleSession(int width, int length, int levels = 1)
  {
    Board = new BattleBoardState(width, length, levels);
  }

  public BattleUnitState AddUnit(Combatant combatant, Vector3I position, Weapon? equippedWeapon = null)
  {
    if (Phase == BattlePhase.Ended)
      throw new InvalidOperationException("Cannot add units after the battle has ended.");
    if (!Board.CanOccupy(position))
      throw new InvalidOperationException($"Cannot place a unit at {position}.");

    var unit = new BattleUnitState(_nextUnitId++, combatant, position, equippedWeapon);
    _units.Add(unit.UnitId, unit);
    Board.GetTile(position).TrySetOccupant(unit.UnitId);

    if (Phase == BattlePhase.InProgress)
      ReconcileTurnQueue();

    Publish(new BattleEvent(BattleEventType.UnitAdded, unit.UnitId, position));
    return unit;
  }

  public bool TryGetUnit(int unitId, out BattleUnitState? unit)
  {
    return _units.TryGetValue(unitId, out unit);
  }

  public IEnumerable<BattleUnitState> GetUnitsForSide(Faction side)
  {
    return _units.Values.Where(unit => unit.Side == side);
  }

  public bool HasLivingUnits(Faction side)
  {
    return GetUnitsForSide(side).Any(unit => unit.IsAlive);
  }

  public void StartBattle()
  {
    if (Phase != BattlePhase.Setup)
      throw new InvalidOperationException("BattleSession can only be started from setup.");

    Phase = BattlePhase.InProgress;
    TurnNumber = 1;
    RebuildTurnQueueFromUnits();

    if (_turnQueue.Count == 0)
      throw new InvalidOperationException("Cannot start a battle without at least one living faction in the session.");

    ActiveSide = _turnQueue.Peek();

    foreach (var unit in _units.Values)
    {
      unit.RefreshForNewTurn();
    }

    SelectFirstUnitForActiveSide();

    Publish(new BattleEvent(BattleEventType.SessionStarted, Message: "Battle started."));
    Publish(new BattleEvent(BattleEventType.TurnStarted, Message: $"Turn {TurnNumber} started for {ActiveSide?.Name}."));
  }

  public void EndBattle()
  {
    if (Phase == BattlePhase.Ended)
      return;

    DeselectCurrentUnit();
    ActiveSide = null;
    Phase = BattlePhase.Ended;
    Publish(new BattleEvent(BattleEventType.SessionEnded, Message: "Battle ended."));
  }

  public bool TrySelectUnit(int unitId)
  {
    if (!TryGetUnit(unitId, out var unit) || unit == null || !unit.IsAlive)
      return false;

    if (Phase == BattlePhase.InProgress && ActiveSide != null && unit.Side != ActiveSide)
      return false;

    DeselectCurrentUnit();
    unit.IsSelected = true;
    SelectedUnitId = unitId;

    Publish(new BattleEvent(BattleEventType.UnitSelected, unitId, unit.Position));
    return true;
  }

  public bool TryMoveSelectedUnitStep(Vector3I destination)
  {
    if (SelectedUnit == null)
      return false;

    return TryMoveSelectedUnitStep(destination, DefaultMovementStepActionPointCost);
  }

  public bool TryMoveSelectedUnitStep(Vector3I destination, int actionPointCost)
  {
    if (SelectedUnit == null)
      return false;

    return TryMoveUnitStep(SelectedUnit.UnitId, destination, actionPointCost);
  }

  public bool TryMoveUnitStep(int unitId, Vector3I destination)
  {
    return TryMoveUnitStep(unitId, destination, DefaultMovementStepActionPointCost);
  }

  public bool TryMoveUnitStep(int unitId, Vector3I destination, int actionPointCost)
  {
    if (Phase != BattlePhase.InProgress)
      return false;
    if (!TryGetUnit(unitId, out var unit) || unit == null || !unit.IsAlive)
      return false;
    if (ActiveSide == null)
      return false;
    if (unit.Side != ActiveSide)
      return false;
    if (!IsAdjacent(unit.Position, destination))
      return false;
    if (!Board.CanOccupy(destination))
      return false;
    if (!unit.TrySpendActionPoints(actionPointCost))
      return false;

    var sourceTile = Board.GetTile(unit.Position);
    var destinationTile = Board.GetTile(destination);
    sourceTile.ClearOccupant();
    destinationTile.TrySetOccupant(unit.UnitId);
    unit.MoveTo(destination);

    Publish(new BattleEvent(BattleEventType.UnitMoved, unit.UnitId, destination));
    return true;
  }

  public void AdvanceTurn()
  {
    if (Phase != BattlePhase.InProgress)
      throw new InvalidOperationException("Cannot advance turn unless the battle is in progress.");
    if (ActiveSide == null)
      throw new InvalidOperationException("Cannot advance turn without an active faction.");

    Publish(new BattleEvent(BattleEventType.TurnEnded, Message: $"Turn {TurnNumber} ended for {ActiveSide.Name}."));
    _sidesActedThisRound.Add(ActiveSide);

    RotateTurnQueue();
    ReconcileTurnQueue();

    if (_turnQueue.Count == 0)
    {
      EndBattle();
      return;
    }

    if (HaveAllQueuedSidesActed())
    {
      TurnNumber++;
      _sidesActedThisRound.Clear();
    }

    ActiveSide = _turnQueue.Peek();

    foreach (var unit in GetUnitsForSide(ActiveSide).Where(unit => unit.IsAlive))
    {
      unit.RefreshForNewTurn();
    }

    SelectFirstUnitForActiveSide();

    Publish(new BattleEvent(BattleEventType.ActiveSideChanged, Message: $"Active side is now {ActiveSide.Name}."));
    Publish(new BattleEvent(BattleEventType.TurnStarted, Message: $"Turn {TurnNumber} started for {ActiveSide.Name}."));
  }

  public void ApplyDamage(int unitId, int amount)
  {
    if (!TryGetUnit(unitId, out var unit) || unit == null)
      throw new InvalidOperationException($"Unknown unit id {unitId}.");

    unit.ReceiveDamage(amount);
    Publish(new BattleEvent(BattleEventType.UnitDamaged, unitId, unit.Position, $"Damage: {amount}"));

    if (!unit.IsAlive)
    {
      Board.GetTile(unit.Position).ClearOccupant();
      if (SelectedUnitId == unitId)
        DeselectCurrentUnit();

      ReconcileTurnQueue();
    }
  }

  private void SelectFirstUnitForActiveSide()
  {
    DeselectCurrentUnit();
    if (ActiveSide == null)
      return;

    var firstUnit = GetUnitsForSide(ActiveSide)
      .Where(unit => unit.IsAlive)
      .OrderBy(unit => unit.UnitId)
      .FirstOrDefault();

    if (firstUnit != null)
      TrySelectUnit(firstUnit.UnitId);
  }

  private void DeselectCurrentUnit()
  {
    if (!SelectedUnitId.HasValue)
      return;

    if (_units.TryGetValue(SelectedUnitId.Value, out var selectedUnit))
      selectedUnit.IsSelected = false;

    SelectedUnitId = null;
  }

  private static bool IsAdjacent(Vector3I source, Vector3I destination)
  {
    Vector3I delta = source - destination;
    return Mathf.Abs(delta.X) + Mathf.Abs(delta.Y) + Mathf.Abs(delta.Z) == 1;
  }

  private void Publish(BattleEvent battleEvent)
  {
    EventRaised?.Invoke(battleEvent);
  }

  private void RebuildTurnQueueFromUnits()
  {
    _turnQueue.Clear();
    _queuedSides.Clear();
    _sidesActedThisRound.Clear();

    foreach (var side in _units.Values
      .Where(unit => unit.IsAlive)
      .OrderBy(unit => unit.UnitId)
      .Select(unit => unit.Side)
      .Distinct())
    {
      EnqueueSide(side);
    }
  }

  private void RotateTurnQueue()
  {
    if (_turnQueue.Count == 0)
      return;

    var currentSide = _turnQueue.Dequeue();
    _queuedSides.Remove(currentSide);

    if (HasLivingUnits(currentSide))
      EnqueueSide(currentSide);
  }

  private void ReconcileTurnQueue()
  {
    var existingOrder = _turnQueue.ToArray();
    _turnQueue.Clear();
    _queuedSides.Clear();

    foreach (var side in existingOrder)
    {
      if (HasLivingUnits(side))
        EnqueueSide(side);
    }

    foreach (var side in _units.Values
      .Where(unit => unit.IsAlive)
      .OrderBy(unit => unit.UnitId)
      .Select(unit => unit.Side)
      .Distinct())
    {
      EnqueueSide(side);
    }

    _sidesActedThisRound.RemoveWhere(side => !_queuedSides.Contains(side));
  }

  private bool HaveAllQueuedSidesActed()
  {
    return _queuedSides.Count > 0 && _queuedSides.All(_sidesActedThisRound.Contains);
  }

  private void EnqueueSide(Faction side)
  {
    if (_queuedSides.Add(side))
      _turnQueue.Enqueue(side);
  }
}
