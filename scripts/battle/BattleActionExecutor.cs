using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FunProject.Battle;

public readonly record struct BattleActionEvaluation(
  BattleSessionMutation Mutation,
  bool IsAllowed,
  BattleMutationFailureReason FailureReason = BattleMutationFailureReason.None,
  Option<string> Message = default,
  int ActionPointCost = 0)
{
  public static BattleActionEvaluation Allowed(BattleSessionMutation mutation, int actionPointCost = 0)
  {
    return Allowed(mutation, actionPointCost, None);
  }

  public static BattleActionEvaluation Allowed(BattleSessionMutation mutation, int actionPointCost, string message)
  {
    return Allowed(mutation, actionPointCost, Some(message));
  }

  public static BattleActionEvaluation Allowed(BattleSessionMutation mutation, int actionPointCost, Option<string> message)
  {
    ArgumentNullException.ThrowIfNull(mutation);
    if (actionPointCost < 0)
      throw new ArgumentOutOfRangeException(nameof(actionPointCost), "Action point cost cannot be negative.");

    return new BattleActionEvaluation(mutation, true, BattleMutationFailureReason.None, message, actionPointCost);
  }

  public static BattleActionEvaluation Rejected(
    BattleSessionMutation mutation,
    BattleMutationFailureReason failureReason = BattleMutationFailureReason.Rejected,
    int actionPointCost = 0)
  {
    return Rejected(mutation, None, failureReason, actionPointCost);
  }

  public static BattleActionEvaluation Rejected(
    BattleSessionMutation mutation,
    string message,
    BattleMutationFailureReason failureReason = BattleMutationFailureReason.Rejected,
    int actionPointCost = 0)
  {
    return Rejected(mutation, Some(message), failureReason, actionPointCost);
  }

  public static BattleActionEvaluation Rejected(
    BattleSessionMutation mutation,
    Option<string> message,
    BattleMutationFailureReason failureReason = BattleMutationFailureReason.Rejected,
    int actionPointCost = 0)
  {
    ArgumentNullException.ThrowIfNull(mutation);
    if (failureReason == BattleMutationFailureReason.None)
      throw new ArgumentOutOfRangeException(nameof(failureReason), "Rejected actions must specify a failure reason.");
    if (actionPointCost < 0)
      throw new ArgumentOutOfRangeException(nameof(actionPointCost), "Action point cost cannot be negative.");

    return new BattleActionEvaluation(mutation, false, failureReason, message, actionPointCost);
  }
}

public sealed class BattleActionExecutor
{
  private abstract record UnitActionContext;
  private sealed record ValidUnitActionContext(BattleUnitState Unit) : UnitActionContext;
  private sealed record InvalidUnitActionContext(BattleActionEvaluation Failure) : UnitActionContext;

  private readonly BattleSession _session;
  private readonly Queue<BattleSessionMutation> _pending = [];

  public int PendingCount => _pending.Count;
  public bool IsBusy { get; private set; }
  public Option<BattleSessionMutation> ActiveMutation { get; private set; }
  public Option<BattleMutationResult> LastResult { get; private set; }

  public event Action<BattleSessionMutation> MutationStarted = delegate { };
  public event Action<BattleMutationResult> MutationResolved = delegate { };

  public BattleActionExecutor(BattleSession session)
  {
    ArgumentNullException.ThrowIfNull(session);
    _session = session;
  }

  public void Enqueue(BattleSessionMutation mutation)
  {
    ArgumentNullException.ThrowIfNull(mutation);
    _pending.Enqueue(mutation);
  }

  public void EnqueueRange(IEnumerable<BattleSessionMutation> mutations)
  {
    ArgumentNullException.ThrowIfNull(mutations);

    foreach (BattleSessionMutation mutation in mutations)
    {
      ArgumentNullException.ThrowIfNull(mutation);
      Enqueue(mutation);
    }
  }

  public Option<BattleMutationResult> Tick()
  {
    if (IsBusy || _pending.Count == 0)
      return None;

    BattleSessionMutation activeMutation = _pending.Dequeue();
    ActiveMutation = Some(activeMutation);
    IsBusy = true;

    BattleMutationResult result;
    try
    {
      MutationStarted.Invoke(activeMutation);
      result = ExecuteNow(activeMutation);
    }
    catch (Exception exception)
    {
      result = BattleMutationResult.Failure(
        activeMutation,
        BattleMutationFailureReason.UnexpectedError,
        exception.Message);
    }

    LastResult = Some(result);
    ActiveMutation = None;
    IsBusy = false;
    MutationResolved.Invoke(result);
    return Some(result);
  }

  public BattleActionEvaluation Evaluate(BattleSessionMutation mutation)
  {
    ArgumentNullException.ThrowIfNull(mutation);
    return mutation switch
    {
      MoveUnitStep moveStep => EvaluateMoveUnitStep(moveStep),
      MoveUnit moveUnit => EvaluateMoveUnit(moveUnit),
      ThrowItem throwItem => EvaluateThrowItem(throwItem),
      PassUnit passUnit => EvaluatePassUnit(passUnit),
      EndFactionTurn endFactionTurn => EvaluateEndFactionTurn(endFactionTurn),
      _ => BattleActionEvaluation.Allowed(mutation, GetActionPointCost(mutation)),
    };
  }

  public BattleMutationResult ExecuteNow(BattleSessionMutation mutation)
  {
    ArgumentNullException.ThrowIfNull(mutation);

    BattleActionEvaluation evaluation = Evaluate(mutation);
    if (!evaluation.IsAllowed)
      return BattleMutationResult.Failure(evaluation.Mutation, evaluation.FailureReason, evaluation.Message);

    return evaluation.Mutation.ExecuteUnchecked(_session);
  }

  public IReadOnlyList<BattleMutationResult> DrainQueue(int maxActions = int.MaxValue)
  {
    if (maxActions <= 0)
      throw new ArgumentOutOfRangeException(nameof(maxActions));

    List<BattleMutationResult> results = [];
    while (_pending.Count > 0 && results.Count < maxActions)
    {
      Option<BattleMutationResult> result = Tick();
      bool hasResult = result.Match(
        resultValue =>
        {
          results.Add(resultValue);
          return true;
        },
        () => false);

      if (!hasResult)
        break;
    }

    return results;
  }

  private BattleActionEvaluation EvaluateMoveUnitStep(MoveUnitStep moveStep)
  {
    ArgumentNullException.ThrowIfNull(moveStep);

    UnitActionContext context = ValidateActingUnit(moveStep, moveStep.UnitHandle, moveStep.ActionPointCost);
    if (context is InvalidUnitActionContext invalidContext)
      return invalidContext.Failure;

    BattleUnitState unit = ((ValidUnitActionContext)context).Unit;
    Option<BattleBoardState.ValidatedPoint> unitPointOption = _session.GetUnitPosition(unit);
    if (unitPointOption.IsNone)
      return BattleActionEvaluation.Rejected(moveStep, $"Unit id {unit.UnitId} is not on a valid tile.", actionPointCost: moveStep.ActionPointCost);
    BattleBoardState.ValidatedPoint unitPoint = unitPointOption.IfNone(default(BattleBoardState.ValidatedPoint));
    Vector3I unitPosition = unitPoint.Raw;

    Option<BattleBoardState.ValidatedPoint> destinationPointOption = _session.Board.ValidatePoint(moveStep.Destination);
    if (destinationPointOption.IsNone)
      return BattleActionEvaluation.Rejected(moveStep, $"{moveStep.Destination} cannot be occupied.", actionPointCost: moveStep.ActionPointCost);
    BattleBoardState.ValidatedPoint destinationPoint = destinationPointOption.IfNone(default(BattleBoardState.ValidatedPoint));

    if (!BattleBoardState.AreAdjacent(unitPoint, destinationPoint))
      return BattleActionEvaluation.Rejected(moveStep, $"{moveStep.Destination} is not adjacent to {unitPosition}.", actionPointCost: moveStep.ActionPointCost);
    if (!_session.Board.CanOccupy(destinationPoint))
      return BattleActionEvaluation.Rejected(moveStep, $"{moveStep.Destination} cannot be occupied.", actionPointCost: moveStep.ActionPointCost);

    return BattleActionEvaluation.Allowed(moveStep, moveStep.ActionPointCost);
  }

  private BattleActionEvaluation EvaluateMoveUnit(MoveUnit moveUnit)
  {
    ArgumentNullException.ThrowIfNull(moveUnit);

    UnitActionContext context = ValidateActingUnit(moveUnit, moveUnit.UnitHandle);
    if (context is InvalidUnitActionContext invalidContext)
      return invalidContext.Failure;

    BattleUnitState unit = ((ValidUnitActionContext)context).Unit;
    Option<BattleBoardState.ValidatedPoint> unitPointOption = _session.GetUnitPosition(unit);
    if (unitPointOption.IsNone)
      return BattleActionEvaluation.Rejected(moveUnit, $"Unit id {unit.UnitId} is not on a valid tile");
    BattleBoardState.ValidatedPoint unitPoint = unitPointOption.IfNone(default(BattleBoardState.ValidatedPoint));
    Vector3I unitPosition = unitPoint.Raw;

    if (moveUnit.Path.Count == 0)
      return BattleActionEvaluation.Rejected(moveUnit, "Move unit mutation requires a non-empty path.");
    if (moveUnit.Path[0] != unitPosition)
      return BattleActionEvaluation.Rejected(moveUnit, "Move unit mutation path must start at the unit's current position.");
    if (moveUnit.Path[^1] == unitPosition)
      return BattleActionEvaluation.Allowed(moveUnit, 0, $"Unit already occupies {moveUnit.Path[^1]}.");

    long totalActionPointCost = (long)(moveUnit.Path.Count - 1) * moveUnit.ActionPointCostPerStep;
    if (unit.CurrentActionPoints < totalActionPointCost)
      return BattleActionEvaluation.Rejected(
        moveUnit,
        $"{unit.Combatant.Name} needs {totalActionPointCost} action points but only has {unit.CurrentActionPoints}.",
        actionPointCost: (int)totalActionPointCost);

    BattleBoardState.ValidatedPoint previousPoint = unitPoint;
    Vector3I previousStep = unitPosition;
    foreach (var step in moveUnit.Path.Skip(1))
    {
      Option<BattleBoardState.ValidatedPoint> stepPointOption = _session.Board.ValidatePoint(step);
      if (stepPointOption.IsNone)
        return BattleActionEvaluation.Rejected(
          moveUnit,
          $"Move unit path step {step} leaves the board.",
          actionPointCost: (int)totalActionPointCost);
      BattleBoardState.ValidatedPoint stepPoint = stepPointOption.IfNone(default(BattleBoardState.ValidatedPoint));

      if (!BattleBoardState.AreAdjacent(previousPoint, stepPoint))
        return BattleActionEvaluation.Rejected(
          moveUnit,
          $"Move unit path step {step} is not adjacent to {previousStep}.",
          actionPointCost: (int)totalActionPointCost);

      BattleActionEvaluation destinationEvaluation = EvaluateMoveUnitPathDestination(
        moveUnit,
        step,
        _session.Board.GetTile(stepPoint),
        (int)totalActionPointCost);
      if (!destinationEvaluation.IsAllowed)
        return destinationEvaluation;

      previousStep = step;
      previousPoint = stepPoint;
    }

    return BattleActionEvaluation.Allowed(moveUnit, (int)totalActionPointCost);
  }

  private BattleActionEvaluation EvaluateThrowItem(ThrowItem throwItem)
  {
    ArgumentNullException.ThrowIfNull(throwItem);

    int actionPointCost = throwItem.Item.ActionPointCost;
    UnitActionContext context = ValidateActingUnit(throwItem, throwItem.UnitHandle, actionPointCost);
    if (context is InvalidUnitActionContext invalidContext)
      return invalidContext.Failure;

    BattleUnitState unit = ((ValidUnitActionContext)context).Unit;
    Option<BattleBoardState.ValidatedPoint> unitPointOption = _session.GetUnitPosition(unit);
    if (unitPointOption.IsNone)
      return BattleActionEvaluation.Rejected(throwItem, $"Unit id {unit.UnitId} is not on a valid tile.", actionPointCost: actionPointCost);
    Vector3I unitPosition = unitPointOption.IfNone(default(BattleBoardState.ValidatedPoint)).Raw;

    if (_session.Board.ValidatePoint(throwItem.TargetCell).IsNone)
      return BattleActionEvaluation.Rejected(throwItem, $"{throwItem.TargetCell} is outside the battle board.", actionPointCost: actionPointCost);
    if (!unit.HasInventoryItem(throwItem.Item))
      return BattleActionEvaluation.Rejected(throwItem, $"{unit.Combatant.Name} does not have {throwItem.Item.ItemName}.", actionPointCost: actionPointCost);
    if (throwItem.Item.ConsumesOnUse && throwItem.Item.IsDepleted)
      return BattleActionEvaluation.Rejected(throwItem, $"{throwItem.Item.ItemName} has no charges remaining.", actionPointCost: actionPointCost);
    if (BattleSession.GetGridDistance(unitPosition, throwItem.TargetCell) > throwItem.Item.ThrowRange)
      return BattleActionEvaluation.Rejected(
        throwItem,
        $"{throwItem.TargetCell} is out of range for {throwItem.Item.ItemName}.",
        actionPointCost: actionPointCost);

    return BattleActionEvaluation.Allowed(throwItem, actionPointCost);
  }

  private static BattleActionEvaluation EvaluateMoveUnitPathDestination(
    MoveUnit moveUnit,
    Vector3I step,
    BattleTileState destinationTile,
    int totalActionPointCost)
  {
    ArgumentNullException.ThrowIfNull(moveUnit);
    ArgumentNullException.ThrowIfNull(destinationTile);

    if (!destinationTile.IsWalkable)
      return BattleActionEvaluation.Rejected(
        moveUnit,
        $"Move unit path step {step} enters an unwalkable tile.",
        actionPointCost: totalActionPointCost);

    if (destinationTile.IsOccupied)
      return BattleActionEvaluation.Rejected(
        moveUnit,
        $"Move unit path step {step} enters an occupied tile.",
        actionPointCost: totalActionPointCost);

    return BattleActionEvaluation.Allowed(moveUnit, totalActionPointCost);
  }

  private BattleActionEvaluation EvaluatePassUnit(PassUnit passUnit)
  {
    ArgumentNullException.ThrowIfNull(passUnit);

    UnitActionContext context = ValidateActingUnit(passUnit, passUnit.UnitHandle);
    if (context is InvalidUnitActionContext invalidContext)
      return invalidContext.Failure;

    return BattleActionEvaluation.Allowed(passUnit);
  }

  private BattleActionEvaluation EvaluateEndFactionTurn(EndFactionTurn endFactionTurn)
  {
    ArgumentNullException.ThrowIfNull(endFactionTurn);

    if (_session.Phase != BattlePhase.InProgress)
      return BattleActionEvaluation.Rejected(endFactionTurn, "Battle is not in progress.");

    Faction activeSide = _session.ActiveSide;
    if (activeSide != endFactionTurn.ExpectedActiveSide)
      return BattleActionEvaluation.Rejected(endFactionTurn, $"{endFactionTurn.ExpectedActiveSide.Name} cannot end a turn while {activeSide.Name} is active.");

    return BattleActionEvaluation.Allowed(endFactionTurn);
  }

  private UnitActionContext ValidateActingUnit(BattleSessionMutation mutation, BattleSession.BattleUnitHandle unitHandle, int actionPointCost = 0)
  {
    ArgumentNullException.ThrowIfNull(mutation);
    ArgumentNullException.ThrowIfNull(unitHandle);

    if (_session.Phase != BattlePhase.InProgress)
      return new InvalidUnitActionContext(BattleActionEvaluation.Rejected(mutation, "Battle is not in progress.", actionPointCost: actionPointCost));

    Faction activeSide = _session.ActiveSide;

    Either<BattleQueryFailure, BattleUnitState> unitResult = _session.Queries.Execute(new GetLivingUnit(unitHandle));
    return unitResult.Match<UnitActionContext>(
      unitFailure => new InvalidUnitActionContext(BattleActionEvaluation.Rejected(
        mutation,
        unitFailure.Message,
        ToMutationFailureReason(unitFailure),
        actionPointCost)),
      unit =>
      {
        if (unit.Side != activeSide)
          return new InvalidUnitActionContext(BattleActionEvaluation.Rejected(mutation, $"{unit.Combatant.Name} is not on the active side.", actionPointCost: actionPointCost));

        Either<BattleQueryFailure, bool> availableResult = _session.Queries.Execute(new IsUnitStillAvailableThisTurn(unitHandle));
        return availableResult.Match<UnitActionContext>(
          availableFailure => new InvalidUnitActionContext(BattleActionEvaluation.Rejected(
            mutation,
            availableFailure.Message,
            ToMutationFailureReason(availableFailure),
            actionPointCost)),
          isAvailable =>
          {
            if (!isAvailable)
              return new InvalidUnitActionContext(BattleActionEvaluation.Rejected(mutation, $"{unit.Combatant.Name} is no longer available this turn.", actionPointCost: actionPointCost));
            if (unit.CurrentActionPoints < actionPointCost)
              return new InvalidUnitActionContext(
                BattleActionEvaluation.Rejected(
                  mutation,
                  $"{unit.Combatant.Name} needs {actionPointCost} action points but only has {unit.CurrentActionPoints}.",
                  actionPointCost: actionPointCost));

            return new ValidUnitActionContext(unit);
          });
      });
  }

  private static BattleMutationFailureReason ToMutationFailureReason(BattleQueryFailure failure)
  {
    ArgumentNullException.ThrowIfNull(failure);

    return failure.Reason == BattleQueryFailureReason.UnexpectedError
      ? BattleMutationFailureReason.UnexpectedError
      : BattleMutationFailureReason.Rejected;
  }

  private static int GetActionPointCost(BattleSessionMutation mutation)
  {
    ArgumentNullException.ThrowIfNull(mutation);

    return mutation switch
    {
      MoveUnitStep moveStep => moveStep.ActionPointCost,
      MoveUnit moveUnit => GetMoveUnitActionPointCost(moveUnit),
      ThrowItem throwItem => throwItem.Item.ActionPointCost,
      _ => 0,
    };
  }

  private static int GetMoveUnitActionPointCost(MoveUnit moveUnit)
  {
    ArgumentNullException.ThrowIfNull(moveUnit);

    long totalActionPointCost = (long)Math.Max(moveUnit.Path.Count - 1, 0) * moveUnit.ActionPointCostPerStep;
    return totalActionPointCost > int.MaxValue ? int.MaxValue : (int)totalActionPointCost;
  }
}

