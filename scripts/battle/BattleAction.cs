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

internal enum BattleActionState
{
  Pending,
  Running,
  Done,
}

public abstract class BattleAction
{
  private BattleActionState _state = BattleActionState.Pending;

  public string ActionId { get; }

  protected BattleAction(string actionId)
  {
    if (string.IsNullOrWhiteSpace(actionId))
      throw new ArgumentException("Action id cannot be null or whitespace.", nameof(actionId));

    ActionId = actionId;
  }

  public bool IsDone()
  {
    return _state == BattleActionState.Done;
  }

  internal virtual Option<BattleAction> NextAction(BattleSession session)
  {
    ArgumentNullException.ThrowIfNull(session);
    if (_state != BattleActionState.Pending)
      return None;

    MarkRunning();
    return Some(this);
  }

  internal virtual BattleActionResult Execute(BattleSession session)
  {
    ArgumentNullException.ThrowIfNull(session);
    return BattleActionResult.Failure(this, BattleActionFailureReason.UnsupportedAction, $"{ActionId} cannot be executed directly.");
  }

  private protected Either<BattleActionResult, BattleUnitState> ValidateActingUnit(
    BattleSession session,
    BattleUnitState unit,
    long actionPointCost = 0)
  {
    ArgumentNullException.ThrowIfNull(session);
    ArgumentNullException.ThrowIfNull(unit);
    ArgumentOutOfRangeException.ThrowIfLessThan(actionPointCost, 0);

    Either<BattleActionResult, BattleUnitState> Reject(string message) =>
      Left<BattleActionResult, BattleUnitState>(BattleActionResult.Failure(this, BattleActionFailureReason.Rejected, message));

    if (session.Phase != BattlePhase.InProgress)
      return Reject("Battle is not in progress.");

    Faction activeSide = session.ActiveSide;

    if (!unit.IsAlive)
      return Reject($"Unit {unit.Id} is not alive.");

    if (unit.Side != activeSide)
      return Reject($"{unit.Combatant.Name} is not on the active side.");

    if (!session.IsUnitStillAvailableThisTurn(unit))
      return Reject($"{unit.Combatant.Name} is no longer available this turn.");

    if (unit.CurrentActionPoints < actionPointCost)
      return Reject($"{unit.Combatant.Name} needs {actionPointCost} action points but only has {unit.CurrentActionPoints}.");

    return Right<BattleActionResult, BattleUnitState>(unit);
  }

  internal bool HasStarted => _state != BattleActionState.Pending;

  internal virtual void ConsumeResult(BattleActionResult result)
  {
    MarkDone();
  }

  private protected void MarkRunning()
  {
    _state = BattleActionState.Running;
  }

  private protected void MarkDone()
  {
    _state = BattleActionState.Done;
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

  internal override Option<BattleAction> NextAction(BattleSession session)
  {
    ArgumentNullException.ThrowIfNull(session);
    if (IsDone())
      return None;

    var route = _validatedRoute ?? ValidateRoute(session);
    if (route is null)
      return None;

    return NextValidatedStep(session, route);
  }

  private Option<BattleAction> NextValidatedStep(BattleSession session, Queue<BattleBoardState.ValidatedPoint> remainingSteps)
  {
    if (remainingSteps.Count == 0)
    {
      MarkDone();
      return None;
    }

    if (!Unit.IsAlive)
    {
      MarkDone();
      return None;
    }

    return session.GetUnitPosition(Unit).Match((currUnitPos) =>
    {
      MarkRunning();
      return Some<BattleAction>(
        new MoveUnitStep(
          Unit,
          currUnitPos,
          remainingSteps.Peek(),
          StepAPCost)
      );
    }, () =>
    {
      MarkDone();
      return Option<BattleAction>.None;
    });
  }

  private Queue<BattleBoardState.ValidatedPoint>? ValidateRoute(BattleSession session)
  {
    ArgumentNullException.ThrowIfNull(session);

    if (_requestedDestinations.Count == 0)
    {
      MarkDone();
      return null;
    }

    long apCost = _requestedDestinations.Count * StepAPCost;

    return ValidateActingUnit(session, Unit, apCost).Match(
      failure =>
      {
        MarkDone();
        return (Queue<BattleBoardState.ValidatedPoint>?)null;
      },
      _ =>
      {
        Option<BattleBoardState.ValidatedPoint> currentPointOption = session.GetUnitPosition(Unit);
        if (currentPointOption.IsNone)
        {
          MarkDone();
          return null;
        }

        BattleBoardState.ValidatedPoint previousPoint = currentPointOption.Value();
        Queue<BattleBoardState.ValidatedPoint> validatedSteps = [];

        foreach (Vector3I tile in _requestedDestinations)
        {
          Option<BattleBoardState.ValidatedPoint> stepPointOption = session.Board.ValidatePoint(tile);
          if (stepPointOption.IsNone)
          {
            MarkDone();
            return null;
          }

          BattleBoardState.ValidatedPoint stepPoint = stepPointOption.Value();
          if (!BattleBoardState.AreAdjacent(previousPoint, stepPoint) || !session.Board.CanOccupy(stepPoint))
          {
            MarkDone();
            return null;
          }

          validatedSteps.Enqueue(stepPoint);
          previousPoint = stepPoint;
        }

        _validatedRoute = validatedSteps;
        return validatedSteps;
      });
  }

  internal override void ConsumeResult(BattleActionResult result)
  {
    if (!result.Succeeded)
    {
      MarkDone();
      return;
    }

    var route = _validatedRoute ?? throw new InvalidOperationException("Move route must be validated before consuming step results.");
    route.Dequeue();
    if (route.Count == 0)
      MarkDone();
  }
}
