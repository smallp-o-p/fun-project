using System;
using System.Collections.Generic;
using FunProject.Combatants;
using Godot;
using FunProject.Items;
using FunProject.Weapons;
using System.Linq;

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
    int actionPointCost = 0)
  {
    ArgumentNullException.ThrowIfNull(session);
    ArgumentNullException.ThrowIfNull(unitHandle);
    if (actionPointCost < 0)
      throw new ArgumentOutOfRangeException(nameof(actionPointCost), "Action point cost cannot be negative.");

    if (session.Phase != BattlePhase.InProgress)
      return Left<BattleActionResult, BattleUnitState>(
        BattleActionResult.Failure(this, BattleActionFailureReason.Rejected, "Battle is not in progress."));

    Faction activeSide = session.ActiveSide;

    Either<BattleQueryFailure, BattleUnitState> unitResult = session.Queries.Execute(new GetLivingUnit(unitHandle));
    return unitResult.Match<Either<BattleActionResult, BattleUnitState>>(
      unitFailure => Left<BattleActionResult, BattleUnitState>(
        BattleActionResult.Failure(this, ToActionFailureReason(unitFailure), unitFailure.Message)),
      unit =>
      {
        if (unit.Side != activeSide)
          return Left<BattleActionResult, BattleUnitState>(
            BattleActionResult.Failure(this, BattleActionFailureReason.Rejected, $"{unit.Combatant.Name} is not on the active side."));

        Either<BattleQueryFailure, bool> availableResult = session.Queries.Execute(new IsUnitStillAvailableThisTurn(unitHandle));
        return availableResult.Match<Either<BattleActionResult, BattleUnitState>>(
          availableFailure => Left<BattleActionResult, BattleUnitState>(
            BattleActionResult.Failure(this, ToActionFailureReason(availableFailure), availableFailure.Message)),
          isAvailable =>
          {
            if (!isAvailable)
              return Left<BattleActionResult, BattleUnitState>(
                BattleActionResult.Failure(this, BattleActionFailureReason.Rejected, $"{unit.Combatant.Name} is no longer available this turn."));
            if (unit.CurrentActionPoints < actionPointCost)
              return Left<BattleActionResult, BattleUnitState>(
                BattleActionResult.Failure(
                  this,
                  BattleActionFailureReason.Rejected,
                  $"{unit.Combatant.Name} needs {actionPointCost} action points but only has {unit.CurrentActionPoints}."));

            return Right<BattleActionResult, BattleUnitState>(unit);
          });
      });
  }

  private static BattleActionFailureReason ToActionFailureReason(BattleQueryFailure failure)
  {
    ArgumentNullException.ThrowIfNull(failure);

    return failure.Reason == BattleQueryFailureReason.UnexpectedError
      ? BattleActionFailureReason.UnexpectedError
      : BattleActionFailureReason.Rejected;
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

  public static MoveUnitStep MoveUnitStep(
    BattleSession.BattleUnitHandle unitHandle,
    Vector3I destination,
    int actionPointCost = BattleSession.DefaultMovementStepActionPointCost)
  {
    return new MoveUnitStep(unitHandle, destination, actionPointCost);
  }

  public static MoveUnit MoveUnit(
    BattleSession.BattleUnitHandle unitHandle,
    IEnumerable<Vector3I> path,
    int actionPointCostPerStep = BattleSession.DefaultMovementStepActionPointCost
  )
  {
    return new MoveUnit(unitHandle, path, actionPointCostPerStep);
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
  private int _nextStepIndex = 1;
  private bool _hasValidatedInitialPath;

  public BattleSession.BattleUnitHandle UnitHandle { get; }
  public IReadOnlyList<Vector3I> Path { get; }
  public int ActionPointCostPerStep { get; }

  public MoveUnit(
    BattleSession.BattleUnitHandle unitHandle,
    IEnumerable<Vector3I> path,
    int actionPointCostPerStep = BattleSession.DefaultMovementStepActionPointCost)
    : base("move_unit")
  {
    ArgumentNullException.ThrowIfNull(unitHandle);
    ArgumentNullException.ThrowIfNull(path);
    if (actionPointCostPerStep < 0)
      throw new ArgumentOutOfRangeException(nameof(actionPointCostPerStep), "Action point cost cannot be negative.");

    UnitHandle = unitHandle;
    Path = [.. path];
    ActionPointCostPerStep = actionPointCostPerStep;
  }

  public override Option<BattleAction> NextAction(BattleSession session)
  {
    ArgumentNullException.ThrowIfNull(session);
    if (IsDone())
      return None;

    if (!_hasValidatedInitialPath && !ValidateInitialPath(session))
      return None;

    if (_nextStepIndex >= Path.Count)
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

    Option<BattleBoardState.ValidatedPoint> currentPointOption = session.GetUnitPosition(UnitHandle);
    if (currentPointOption.IsNone)
    {
      MarkCancelled();
      return None;
    }

    Vector3I expectedPosition = Path[_nextStepIndex - 1];
    Vector3I currentPosition = currentPointOption.IfNone(default(BattleBoardState.ValidatedPoint)).Raw;
    if (currentPosition != expectedPosition)
    {
      MarkCancelled();
      return None;
    }

    MarkRunning();
    BattleAction action = new MoveUnitStep(UnitHandle, Path[_nextStepIndex], ActionPointCostPerStep);
    return Some(action);
  }

  private bool ValidateInitialPath(BattleSession session)
  {
    ArgumentNullException.ThrowIfNull(session);

    if (Path.Count == 0)
    {
      MarkFailed();
      return false;
    }

    Option<BattleUnitState> unitOption = session.GetLivingUnit(UnitHandle);
    if (unitOption.IsNone)
    {
      MarkCancelled();
      return false;
    }

    BattleUnitState unit = unitOption.IfNone(default(BattleUnitState));
    long totalActionPointCost = (long)Math.Max(Path.Count - 1, 0) * ActionPointCostPerStep;
    if (unit.CurrentActionPoints < totalActionPointCost)
    {
      MarkFailed();
      return false;
    }

    Option<BattleBoardState.ValidatedPoint> currentPointOption = session.GetUnitPosition(UnitHandle);
    if (currentPointOption.IsNone)
    {
      MarkCancelled();
      return false;
    }

    BattleBoardState.ValidatedPoint previousPoint = currentPointOption.IfNone(default(BattleBoardState.ValidatedPoint));
    if (previousPoint.Raw != Path[0])
    {
      MarkFailed();
      return false;
    }

    for (int stepIndex = 1; stepIndex < Path.Count; stepIndex++)
    {
      Option<BattleBoardState.ValidatedPoint> stepPointOption = session.Board.ValidatePoint(Path[stepIndex]);
      if (stepPointOption.IsNone)
      {
        MarkFailed();
        return false;
      }

      BattleBoardState.ValidatedPoint stepPoint = stepPointOption.IfNone(default(BattleBoardState.ValidatedPoint));
      if (!BattleBoardState.AreAdjacent(previousPoint, stepPoint) || !session.Board.CanOccupy(stepPoint))
      {
        MarkFailed();
        return false;
      }

      previousPoint = stepPoint;
    }

    _hasValidatedInitialPath = true;
    return true;
  }

  public override void ConsumeResult(BattleActionResult result)
  {
    if (!result.Succeeded)
    {
      MarkFailed();
      return;
    }

    _nextStepIndex++;
    if (_nextStepIndex >= Path.Count)
      MarkCompleted();
    else
      MarkPending();
  }
}
