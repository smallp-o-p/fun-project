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
    BattleSession.BattleUnitHandle unitHandle,
    long actionPointCost = 0)
  {
    ArgumentNullException.ThrowIfNull(session);
    ArgumentNullException.ThrowIfNull(unitHandle);
    if (actionPointCost < 0)
      throw new ArgumentOutOfRangeException(nameof(actionPointCost), "Action point cost cannot be negative.");

    if (session.Phase != BattlePhase.InProgress)
      return Left<BattleActionResult, BattleUnitState>(
        BattleActionResult.Failure(this, BattleActionFailureReason.Rejected, "Battle is not in progress."));

    Faction activeSide = session.ActiveSide;

    return session.GetUnit(unitHandle).Match<Either<BattleActionResult, BattleUnitState>>(
      unit =>
      {
        if (!unit.IsAlive)
          return Left<BattleActionResult, BattleUnitState>(
            BattleActionResult.Failure(this, BattleActionFailureReason.Rejected, $"Unit {unitHandle.UnitId} is not alive."));

        if (unit.Side != activeSide)
          return Left<BattleActionResult, BattleUnitState>(
            BattleActionResult.Failure(this, BattleActionFailureReason.Rejected, $"{unit.Combatant.Name} is not on the active side."));

        if (!session.IsUnitStillAvailableThisTurn(unitHandle))
          return Left<BattleActionResult, BattleUnitState>(
            BattleActionResult.Failure(this, BattleActionFailureReason.Rejected, $"{unit.Combatant.Name} is no longer available this turn."));

        if (unit.CurrentActionPoints < actionPointCost)
          return Left<BattleActionResult, BattleUnitState>(
            BattleActionResult.Failure(
              this,
              BattleActionFailureReason.Rejected,
              $"{unit.Combatant.Name} needs {actionPointCost} action points but only has {unit.CurrentActionPoints}."));

        return Right<BattleActionResult, BattleUnitState>(unit);
      },
      () => Left<BattleActionResult, BattleUnitState>(
        BattleActionResult.Failure(this, BattleActionFailureReason.Rejected, $"Unknown unit id {unitHandle.UnitId}.")));
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
    BattleSession.BattleUnitHandle unitHandle,
    IEnumerable<Vector3I> destinations,
    int actionPointCostPerStep = BattleSession.DefaultMovementStepActionPointCost
  )
  {
    return new MoveUnit(unitHandle, destinations, actionPointCostPerStep);
  }

  public static ThrowItem ThrowItem(BattleSession.BattleUnitHandle unitHandle, ThrowableItem item, Vector3I targetCell)
  {
    return new ThrowItem(unitHandle, item, targetCell);
  }

  public static ApplyDamage ApplyDamage(BattleSession.BattleUnitHandle unitHandle, int amount)
  {
    return new ApplyDamage(unitHandle, amount);
  }

  public static PassUnit PassUnit(BattleSession.BattleUnitHandle unitHandle)
  {
    return new PassUnit(unitHandle);
  }

  public static EndFactionTurn EndFactionTurn(Faction expectedActiveSide)
  {
    return new EndFactionTurn(expectedActiveSide);
  }
}

public sealed class MoveUnit : BattleAction
{
  private Either<IReadOnlyList<Vector3I>, Queue<BattleBoardState.ValidatedPoint>> _routeState;

  public BattleSession.BattleUnitHandle UnitHandle { get; }
  public int StepAPCost { get; }

  public MoveUnit(
    BattleSession.BattleUnitHandle unitHandle,
    IEnumerable<Vector3I> destinations,
    int actionPointCostPerStep = BattleSession.DefaultMovementStepActionPointCost)
    : base("move_unit")
  {
    ArgumentNullException.ThrowIfNull(unitHandle);
    ArgumentNullException.ThrowIfNull(destinations);
    ArgumentOutOfRangeException.ThrowIfLessThan(actionPointCostPerStep, 0);

    UnitHandle = unitHandle;
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

      Option<BattleUnitState> unitOption = session.GetLivingUnit(UnitHandle);
      if (unitOption.IsNone)
      {
        MarkCancelled();
        return None;
      }

      return session.GetUnitPosition(UnitHandle).Match((currUnitPos) =>
      {
        MarkRunning();
        return Some<BattleAction>(
          new MoveUnitStep(
            UnitHandle,
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

    return ValidateActingUnit(session, UnitHandle, apCost).Match(
      failure =>
      {
        MarkFailed();
        return None;
      },
      _ =>
      {
        Option<BattleBoardState.ValidatedPoint> currentPointOption = session.GetUnitPosition(UnitHandle);
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
