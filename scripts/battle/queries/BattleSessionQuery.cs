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

  internal abstract BattleQueryResult<TResult> Execute(BattleSession session);

  protected BattleQueryResult<TResult> Succeed(TResult value)
  {
    return BattleQueryResult<TResult>.Success(QueryId, value);
  }

  protected BattleQueryResult<TResult> Fail(BattleQueryFailureReason reason, string message)
  {
    return BattleQueryResult<TResult>.Failed(QueryId, reason, message);
  }
}
