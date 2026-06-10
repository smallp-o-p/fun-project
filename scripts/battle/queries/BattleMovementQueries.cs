using Godot;
using LanguageExt.UnsafeValueAccess;
using System;
using System.Collections.Generic;

namespace FunProject.Battle;

public sealed class FindPathForUnit : BattleSessionQuery<BattleBoardState.ValidatedPoint[]>
{
  public BattleUnitState Unit { get; }
  public Vector3I Destination { get; }

  public FindPathForUnit(BattleUnitState unit, Vector3I destination)
  {
    ArgumentNullException.ThrowIfNull(unit);
    Unit = unit;
    Destination = destination;
  }

  internal override Either<BattleQueryFailure, BattleBoardState.ValidatedPoint[]> Execute(BattleSession session)
  {
    if (!Unit.IsAlive)
      return FailUnitNotAlive(Unit);

    Option<BattleBoardState.ValidatedPoint> unitPointOption = session.GetUnitPosition(Unit);
    Option<BattleBoardState.ValidatedPoint> destinationPointOption = session.Board.ValidatePoint(Destination);

    if (unitPointOption.IsNone)
      return Fail(BattleQueryFailureReason.InvalidTile, $"Unit {Unit.Id} is not on the board.");
    if (destinationPointOption.IsNone)
      return Fail(BattleQueryFailureReason.InvalidTile, $"Destination {Destination} is outside the battle board.");

    BattleBoardState.ValidatedPoint unitPoint = unitPointOption.Value();
    BattleBoardState.ValidatedPoint destinationPoint = destinationPointOption.Value();

    return Succeed(session.Board.FindPath(unitPoint, destinationPoint, Unit.Id));
  }
}

public sealed class GetPossibleMoveTilesForUnit : BattleSessionQuery<IReadOnlyCollection<BattleBoardState.ValidatedPoint>>
{
  public BattleUnitState Unit { get; }
  public int ActionPointCostPerStep { get; }

  public GetPossibleMoveTilesForUnit(
    BattleUnitState unit,
    int actionPointCostPerStep = BattleSession.DefaultMovementStepActionPointCost)
  {
    ArgumentNullException.ThrowIfNull(unit);
    Unit = unit;
    if (actionPointCostPerStep < 0)
      throw new ArgumentOutOfRangeException(nameof(actionPointCostPerStep), "Action point cost cannot be negative.");

    ActionPointCostPerStep = actionPointCostPerStep;
  }

  internal override Either<BattleQueryFailure, IReadOnlyCollection<BattleBoardState.ValidatedPoint>> Execute(BattleSession session)
  {
    if (!Unit.IsAlive)
      return FailUnitNotAlive(Unit);
    if (Unit.CurrentActionPoints < ActionPointCostPerStep)
      return Succeed([]);

    Option<BattleBoardState.ValidatedPoint> unitPointOption = session.GetUnitPosition(Unit);
    if (unitPointOption.IsNone)
      return Fail(BattleQueryFailureReason.InvalidTile, $"Unit {Unit.Id} is not on the board.");

    // Zero step cost means an unlimited budget; the visited set still bounds the fill to the board.
    int maxSteps = ActionPointCostPerStep == 0
      ? int.MaxValue
      : Unit.CurrentActionPoints / ActionPointCostPerStep;
    return Succeed(session.Board.GetReachableTiles(unitPointOption.Value(), maxSteps));
  }
}
