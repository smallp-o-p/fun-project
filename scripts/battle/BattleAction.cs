using System;
using System.Collections.Generic;
using FunProject.Combatants;
using Godot;
using FunProject.Items;
using FunProject.Weapons;
using System.Linq;
using LanguageExt.UnsafeValueAccess;

namespace FunProject.Battle;

internal enum BattleActionState
{
  Pending,
  Running,
  Completed,
  Cancelled,
  Failed,
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
    return _state is BattleActionState.Completed or BattleActionState.Cancelled or BattleActionState.Failed;
  }

  public virtual Option<BattleAction> NextAction(BattleSession session)
  {
    ArgumentNullException.ThrowIfNull(session);
    if (_state != BattleActionState.Pending)
      return None;

    MarkRunning();
    return Some(this);
  }

  public virtual BattleActionResult Execute(BattleSession session)
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

    if (!unit.BelongsTo(session))
      return Left<BattleActionResult, BattleUnitState>(
        BattleActionResult.Failure(this, BattleActionFailureReason.Rejected, $"Unknown unit id {unit.Id}."));

    if (session.Phase != BattlePhase.InProgress)
      return Left<BattleActionResult, BattleUnitState>(
        BattleActionResult.Failure(this, BattleActionFailureReason.Rejected, "Battle is not in progress."));

    Faction activeSide = session.ActiveSide;

    if (!unit.IsAlive)
    {
      return Left<BattleActionResult, BattleUnitState>(
        BattleActionResult.Failure(this, BattleActionFailureReason.Rejected, $"Unit {unit.Id} is not alive."));
    }

    if (unit.Side != activeSide)
    {
      return Left<BattleActionResult, BattleUnitState>(
        BattleActionResult.Failure(this, BattleActionFailureReason.Rejected, $"{unit.Combatant.Name} is not on the active side."));
    }

    if (!session.IsUnitStillAvailableThisTurn(unit))
    {
      return Left<BattleActionResult, BattleUnitState>(
        BattleActionResult.Failure(this, BattleActionFailureReason.Rejected, $"{unit.Combatant.Name} is no longer available this turn."));
    }

    if (unit.CurrentActionPoints < actionPointCost)
    {
      return Left<BattleActionResult, BattleUnitState>(
        BattleActionResult.Failure(
          this,
          BattleActionFailureReason.Rejected,
          $"{unit.Combatant.Name} needs {actionPointCost} action points but only has {unit.CurrentActionPoints}."));
    }

    return Right<BattleActionResult, BattleUnitState>(unit);
  }

  public virtual void ConsumeResult(BattleActionResult result)
  {
    if (result.Succeeded)
      MarkCompleted();
    else
      MarkFailed();
  }

  private protected void MarkPending()
  {
    _state = BattleActionState.Pending;
  }

  private protected void MarkRunning()
  {
    _state = BattleActionState.Running;
  }

  private protected void MarkCompleted()
  {
    _state = BattleActionState.Completed;
  }

  private protected void MarkCancelled()
  {
    _state = BattleActionState.Cancelled;
  }

  private protected void MarkFailed()
  {
    _state = BattleActionState.Failed;
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

  public static ThrowItem ThrowItem(BattleUnitState unit, ThrowableItem item, Vector3I targetCell)
  {
    return new ThrowItem(unit, item, targetCell);
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
  private Either<IReadOnlyList<Vector3I>, Queue<BattleBoardState.ValidatedPoint>> _routeState;

  public BattleUnitState Unit { get; }
  public int StepAPCost { get; }

  public MoveUnit(
    BattleUnitState unit,
    IEnumerable<Vector3I> destinations,
    int actionPointCostPerStep = BattleSession.DefaultMovementStepActionPointCost)
    : base("move_unit")
  {
    ArgumentNullException.ThrowIfNull(unit);
    ArgumentNullException.ThrowIfNull(destinations);
    ArgumentOutOfRangeException.ThrowIfLessThan(actionPointCostPerStep, 0);

    Unit = unit;
    _routeState = Left<IReadOnlyList<Vector3I>, Queue<BattleBoardState.ValidatedPoint>>([.. destinations]);
    StepAPCost = actionPointCostPerStep;
  }

  public override Option<BattleAction> NextAction(BattleSession session)
  {
    ArgumentNullException.ThrowIfNull(session);
    if (IsDone())
      return None;

    Option<BattleAction> NextValidatedStep(Queue<BattleBoardState.ValidatedPoint> remainingSteps)
    {
      if (remainingSteps.Count == 0)
      {
        MarkCompleted();
        return None;
      }

      if (!Unit.BelongsTo(session) || !Unit.IsAlive)
      {
        MarkCancelled();
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
        MarkCancelled();
        return Option<BattleAction>.None;
      });
    }

    return _routeState.Match(
      rawDestinations => ValidateRoute(session, rawDestinations).Match(NextValidatedStep, None),
      NextValidatedStep);
  }

  private Option<Queue<BattleBoardState.ValidatedPoint>> ValidateRoute(BattleSession session, IReadOnlyList<Vector3I> tilesToOccupy)
  {
    ArgumentNullException.ThrowIfNull(session);

    if (tilesToOccupy.Count == 0)
    {
      MarkFailed();
      return None;
    }

    long apCost = tilesToOccupy.Count * StepAPCost;

    return ValidateActingUnit(session, Unit, apCost).Match(
      failure =>
      {
        MarkFailed();
        return None;
      },
      _ =>
      {
        Option<BattleBoardState.ValidatedPoint> currentPointOption = session.GetUnitPosition(Unit);
        if (currentPointOption.IsNone)
        {
          MarkCancelled();
          return None;
        }

        BattleBoardState.ValidatedPoint previousPoint = currentPointOption.Value();
        Queue<BattleBoardState.ValidatedPoint> validatedSteps = [];

        foreach (Vector3I tile in tilesToOccupy)
        {
          Option<BattleBoardState.ValidatedPoint> stepPointOption = session.Board.ValidatePoint(tile);
          if (stepPointOption.IsNone)
          {
            MarkFailed();
            return None;
          }

          BattleBoardState.ValidatedPoint stepPoint = stepPointOption.Value();
          if (!BattleBoardState.AreAdjacent(previousPoint, stepPoint) || !session.Board.CanOccupy(stepPoint))
          {
            MarkFailed();
            return None;
          }

          validatedSteps.Enqueue(stepPoint);
          previousPoint = stepPoint;
        }

        _routeState = Right<IReadOnlyList<Vector3I>, Queue<BattleBoardState.ValidatedPoint>>(validatedSteps);
        return Some(validatedSteps);
      });
  }

  public override void ConsumeResult(BattleActionResult result)
  {
    if (!result.Succeeded)
    {
      MarkFailed();
      return;
    }

    bool routeComplete = _routeState.Match(
      _ => throw new InvalidOperationException("Move route must be validated before consuming step results."),
      remainingSteps =>
      {
        remainingSteps.Dequeue();
        return remainingSteps.Count == 0;
      });

    if (routeComplete)
      MarkCompleted();
    else
      MarkPending();
  }
}
