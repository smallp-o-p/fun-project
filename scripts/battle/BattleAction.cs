using System;
using System.Collections.Generic;
using FunProject.Combatants;
using FunProject.Items;
using FunProject.Items.Capabilities;
using FunProject.Weapons;
using Godot;
using System.Linq;
using LanguageExt.UnsafeValueAccess;

namespace FunProject.Battle;

public abstract class BattleAction
{
  private bool _started;
  private bool _finished;

  public string ActionId { get; }

  protected BattleAction(string actionId)
  {
    if (string.IsNullOrWhiteSpace(actionId))
      throw new ArgumentException("Action id cannot be null or whitespace.", nameof(actionId));

    ActionId = actionId;
  }

  public virtual bool IsDone()
  {
    return _finished;
  }

  internal virtual Option<BattleAction> NextAction(BattleSession session)
  {
    ArgumentNullException.ThrowIfNull(session);
    if (_started)
      return None;

    _started = true;
    return Some(this);
  }

  internal virtual BattleActionResult Execute(BattleSession session)
  {
    ArgumentNullException.ThrowIfNull(session);
    return BattleActionResult.Failure(this, BattleActionFailureReason.UnsupportedAction, $"{ActionId} cannot be executed directly.");
  }

  private protected Option<BattleActionResult> ValidateActingUnit(
    BattleSession session,
    BattleUnitState unit,
    long actionPointCost = 0)
  {
    ArgumentNullException.ThrowIfNull(session);
    ArgumentNullException.ThrowIfNull(unit);
    ArgumentOutOfRangeException.ThrowIfLessThan(actionPointCost, 0);

    Option<BattleActionResult> Reject(string message) =>
      Some(BattleActionResult.Failure(this, BattleActionFailureReason.Rejected, message));

    if (session.Phase != BattlePhase.InProgress)
      return Reject("Battle is not in progress.");

    Faction activeSide = session.ActiveSide;

    if (!unit.IsAlive)
      return Reject($"Unit {unit.Id} is not alive.");

    if (unit.Side != activeSide)
      return Reject($"{unit.Combatant.Name} is not on the active side.");

    if (!session.IsUnitStillAvailableThisTurn(unit))
      return Reject($"{unit.Combatant.Name} is no longer available this turn.");

    if (unit.IsImmobilized)
      return Reject($"{unit.Combatant.Name} is immobilized and cannot act.");

    if (unit.CurrentActionPoints < actionPointCost)
      return Reject($"{unit.Combatant.Name} needs {actionPointCost} action points but only has {unit.CurrentActionPoints}.");

    return None;
  }

  internal virtual bool HasStarted => _started;

  internal virtual void ConsumeResult(BattleActionResult result)
  {
    _finished = true;
  }

  public static StartBattle StartBattle()
  {
    return new StartBattle();
  }

  public static SpawnUnit SpawnUnit(Combatant combatant, Vector3I position)
  {
    return new SpawnUnit(combatant, position);
  }

  public static SpawnUnit SpawnUnit(Combatant combatant, Vector3I position, Weapon equippedWeapon)
  {
    return new SpawnUnit(combatant, position, equippedWeapon);
  }

  public static SpawnUnit SpawnUnit(
    Combatant combatant,
    Vector3I position,
    Weapon equippedWeapon,
    ItemWith<ArmorCapability> equippedArmor)
  {
    return new SpawnUnit(combatant, position, Some(equippedWeapon), Some(equippedArmor));
  }

  public static MoveUnit MoveUnit(
    BattleUnitState unit,
    IEnumerable<Vector3I> destinations,
    int actionPointCostPerStep = BattleSession.DefaultMovementStepActionPointCost
  )
  {
    return new MoveUnit(unit, destinations, actionPointCostPerStep);
  }

  public static ThrowItem ThrowItem(BattleUnitState unit, ItemWith<ThrowableCapability> throwable, Vector3I targetCell)
  {
    return new ThrowItem(unit, throwable, targetCell);
  }

  public static AttackUnit AttackUnit(BattleUnitState unit, BattleUnitState target)
  {
    return new AttackUnit(unit, target);
  }

  public static ReloadWeapon ReloadWeapon(BattleUnitState unit)
  {
    return new ReloadWeapon(unit);
  }

  public static ApplyDamage ApplyDamage(BattleUnitState unit, int amount)
  {
    return new ApplyDamage(unit, amount);
  }

  public static PassUnit PassUnit(BattleUnitState unit)
  {
    return new PassUnit(unit);
  }

  public static EndFactionTurn EndFactionTurn(Faction expectedActiveSide)
  {
    return new EndFactionTurn(expectedActiveSide);
  }
}

public sealed class MoveUnit : BattleAction
{
  private readonly IReadOnlyList<Vector3I> _requestedDestinations;
  private Queue<BattleBoardState.ValidatedPoint>? _validatedRoute;
  private bool _validationAttempted;

  public BattleUnitState Unit { get; }
  public int StepAPCost { get; }

  internal MoveUnit(
    BattleUnitState unit,
    IEnumerable<Vector3I> destinations,
    int actionPointCostPerStep = BattleSession.DefaultMovementStepActionPointCost)
    : base("move_unit")
  {
    ArgumentNullException.ThrowIfNull(unit);
    ArgumentNullException.ThrowIfNull(destinations);
    ArgumentOutOfRangeException.ThrowIfLessThan(actionPointCostPerStep, 0);

    Unit = unit;
    _requestedDestinations = [.. destinations];
    StepAPCost = actionPointCostPerStep;
  }

  // The validated-route queue is the single source of truth for the lifecycle; there
  // are no manual state transitions. Aborts (failed validation, a failed step, or the
  // mover dying/leaving the board mid-route) drain the queue so the same derivation
  // covers every "nothing left to do" case:
  //   - validation not yet attempted          => not started, still pending
  //   - attempted but route null / queue empty => done (rejected, exhausted, or aborted)
  //   - non-empty queue                        => started and running
  public override bool IsDone() =>
    _validationAttempted && (_validatedRoute is null || _validatedRoute.Count == 0);

  internal override bool HasStarted => _validationAttempted;

  internal override Option<BattleAction> NextAction(BattleSession session)
  {
    ArgumentNullException.ThrowIfNull(session);
    if (IsDone())
      return None;

    Queue<BattleBoardState.ValidatedPoint>? route = _validatedRoute ?? ValidateRoute(session);
    if (route is null || route.Count == 0)
      return None;

    if (!Unit.IsAlive)
    {
      route.Clear();
      return None;
    }

    return session.GetUnitPosition(Unit).Match(
      currUnitPos => Some<BattleAction>(new MoveUnitStep(Unit, currUnitPos, route.Peek(), StepAPCost)),
      () =>
      {
        route.Clear();
        return Option<BattleAction>.None;
      });
  }

  private Queue<BattleBoardState.ValidatedPoint>? ValidateRoute(BattleSession session)
  {
    ArgumentNullException.ThrowIfNull(session);
    _validationAttempted = true;

    if (_requestedDestinations.Count == 0)
      return null;

    long apCost = (long)_requestedDestinations.Count * StepAPCost;
    if (ValidateActingUnit(session, Unit, apCost).IsSome)
      return null;

    Option<BattleBoardState.ValidatedPoint> currentPointOption = session.GetUnitPosition(Unit);
    if (currentPointOption.IsNone)
      return null;

    BattleBoardState.ValidatedPoint previousPoint = currentPointOption.Value();
    Queue<BattleBoardState.ValidatedPoint> validatedSteps = [];

    foreach (Vector3I tile in _requestedDestinations)
    {
      Option<BattleBoardState.ValidatedPoint> stepPointOption = session.Board.ValidatePoint(tile);
      if (stepPointOption.IsNone)
        return null;

      BattleBoardState.ValidatedPoint stepPoint = stepPointOption.Value();
      if (!BattleBoardState.AreAdjacent(previousPoint, stepPoint) || !session.Board.CanOccupy(stepPoint))
        return null;

      validatedSteps.Enqueue(stepPoint);
      previousPoint = stepPoint;
    }

    _validatedRoute = validatedSteps;
    return validatedSteps;
  }

  internal override void ConsumeResult(BattleActionResult result)
  {
    Queue<BattleBoardState.ValidatedPoint> route = _validatedRoute
      ?? throw new InvalidOperationException("Move route must be validated before consuming step results.");

    // A failed step aborts the whole move; draining the queue marks the action done.
    if (!result.Succeeded)
    {
      route.Clear();
      return;
    }

    route.Dequeue();
  }
}
