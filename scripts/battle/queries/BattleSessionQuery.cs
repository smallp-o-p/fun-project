using System;

namespace FunProject.Battle;

public abstract class BattleSessionQuery<TResult>
{
  internal abstract Either<BattleQueryFailure, TResult> Execute(BattleSession session);

  protected Either<BattleQueryFailure, TResult> Succeed(TResult value)
  {
    return Right<BattleQueryFailure, TResult>(value);
  }

  protected Either<BattleQueryFailure, TResult> Fail(BattleQueryFailureReason reason, string message)
  {
    return Left<BattleQueryFailure, TResult>(new BattleQueryFailure(reason, message));
  }

  protected Either<BattleQueryFailure, TResult> FailUnitNotAlive(BattleUnitState unit)
  {
    return Fail(BattleQueryFailureReason.UnitNotAlive, $"Unit {unit.Id} is not alive.");
  }

  protected Either<BattleQueryFailure, Unit> RequireInProgress(BattleSession session) =>
    session.Phase == BattlePhase.InProgress
      ? Right<BattleQueryFailure, Unit>(unit)
      : Left<BattleQueryFailure, Unit>(new BattleQueryFailure(
          BattleQueryFailureReason.InvalidBattleState,
          "Cannot query while the battle is not in progress."));

  protected Either<BattleQueryFailure, BattleBoardState.ValidatedPoint> RequirePosition(
    BattleSession session, BattleUnitState subject) =>
    session.GetUnitPosition(subject).Match(
      Some: point => Right<BattleQueryFailure, BattleBoardState.ValidatedPoint>(point),
      None: () => Left<BattleQueryFailure, BattleBoardState.ValidatedPoint>(
        new BattleQueryFailure(BattleQueryFailureReason.InvalidTile,
          $"Unit {subject.Id} is not on the board.")));
}
