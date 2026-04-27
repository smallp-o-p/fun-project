using System;

namespace FunProject.Battle;

public abstract class BattleSessionQuery<TResult>
{
  public string QueryId { get; }

  protected BattleSessionQuery(string queryId)
  {
    if (string.IsNullOrWhiteSpace(queryId))
      throw new ArgumentException("Query id cannot be null or whitespace.", nameof(queryId));

    QueryId = queryId;
  }

  internal abstract Either<BattleQueryFailure, TResult> Execute(BattleSession session);

  protected Either<BattleQueryFailure, TResult> Succeed(TResult value)
  {
    return Right<BattleQueryFailure, TResult>(value);
  }

  protected Either<BattleQueryFailure, TResult> Fail(BattleQueryFailureReason reason, string message)
  {
    return Left<BattleQueryFailure, TResult>(new BattleQueryFailure(reason, message));
  }

  protected Either<BattleQueryFailure, TResult> Fail(BattleQueryFailure failure)
  {
    ArgumentNullException.ThrowIfNull(failure);
    return Left<BattleQueryFailure, TResult>(failure);
  }
}
