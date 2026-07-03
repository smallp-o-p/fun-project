
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

  protected Either<BattleQueryFailure, Unit> RequireInProgress(BattleSession session) =>
    session.Phase == BattlePhase.InProgress
      ? Right<BattleQueryFailure, Unit>(unit)
      : Left<BattleQueryFailure, Unit>(new BattleQueryFailure(
          BattleQueryFailureReason.InvalidBattleState,
          "Cannot query while the battle is not in progress."));
}
