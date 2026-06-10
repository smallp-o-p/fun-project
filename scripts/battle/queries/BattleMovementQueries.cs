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

    BattleBoardState.ValidatedPoint unitPoint = unitPointOption.Value();

    if (ActionPointCostPerStep == 0)
    {
      List<BattleBoardState.ValidatedPoint> allReachable = [];
      foreach (BattleBoardState.ValidatedPoint point in session.Board.EnumerateBoardPoints())
      {
        if (point == unitPoint || !session.Board.CanOccupy(point))
          continue;
        if (session.Board.FindPath(unitPoint, point, Unit.Id).Length > 0)
          allReachable.Add(point);
      }
      return Succeed(allReachable);
    }

    int maxSteps = Unit.CurrentActionPoints / ActionPointCostPerStep;
    return Succeed(FloodFillReachableTiles(session.Board, unitPoint, Unit.Id, maxSteps));
  }

  private static List<BattleBoardState.ValidatedPoint> FloodFillReachableTiles(
    BattleBoardState board,
    BattleBoardState.ValidatedPoint origin,
    int movingUnitId,
    int maxSteps)
  {
    List<BattleBoardState.ValidatedPoint> reachable = [];
    System.Collections.Generic.HashSet<BattleBoardState.ValidatedPoint> visited = [origin];
    Queue<(BattleBoardState.ValidatedPoint point, int steps)> queue = new();
    queue.Enqueue((origin, 0));

    while (queue.Count > 0)
    {
      var (current, steps) = queue.Dequeue();
      if (steps >= maxSteps)
        continue;

      foreach (BattleBoardState.ValidatedPoint neighbor in EnumerateOrthogonalNeighbors(board, current))
      {
        if (!visited.Add(neighbor))
          continue;

        BattleTileState tile = board.GetTile(neighbor);
        if (!tile.IsWalkable)
          continue;
        if (tile.IsOccupied && !tile.HasOccupant(movingUnitId))
          continue;

        if (!tile.IsOccupied)
          reachable.Add(neighbor);

        queue.Enqueue((neighbor, steps + 1));
      }
    }

    return reachable;
  }

  private static readonly Vector3I[] OrthogonalOffsets =
  [
    new Vector3I(1, 0, 0),
    new Vector3I(-1, 0, 0),
    new Vector3I(0, 0, 1),
    new Vector3I(0, 0, -1),
    new Vector3I(0, 1, 0),
    new Vector3I(0, -1, 0),
  ];

  private static IEnumerable<BattleBoardState.ValidatedPoint> EnumerateOrthogonalNeighbors(
    BattleBoardState board,
    BattleBoardState.ValidatedPoint point)
  {
    foreach (Vector3I offset in OrthogonalOffsets)
    {
      Option<BattleBoardState.ValidatedPoint> neighbor = board.ValidatePoint(point.Raw + offset);
      if (neighbor.IsSome)
        yield return neighbor.Value();
    }
  }
}
