using System;

namespace FunProject.Battle;

public sealed class BattleQueryRunner
{
  private readonly BattleSession _session;

  internal BattleQueryRunner(BattleSession session)
  {
    ArgumentNullException.ThrowIfNull(session);
    _session = session;
  }

  public BattleQueryResult<TResult> Execute<TResult>(BattleSessionQuery<TResult> query)
  {
    ArgumentNullException.ThrowIfNull(query);

    try
    {
      var result = query.Execute(_session);
      if (result == null)
      {
        return BattleQueryResult<TResult>.Failed(
          query.QueryId,
          BattleQueryFailureReason.InvalidQuery,
          $"Query {query.QueryId} returned no result.");
      }

      return result;
    }
    catch (Exception exception)
    {
      return BattleQueryResult<TResult>.Failed(
        query.QueryId,
        BattleQueryFailureReason.UnexpectedError,
        exception.Message);
    }
  }
}
