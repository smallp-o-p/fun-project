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

  public Either<BattleQueryFailure, TResult> Execute<TResult>(BattleSessionQuery<TResult> query)
  {
    ArgumentNullException.ThrowIfNull(query);

    try
    {
      return query.Execute(_session);
    }
    catch (Exception exception)
    {
      return Left<BattleQueryFailure, TResult>(new BattleQueryFailure(
        BattleQueryFailureReason.UnexpectedError,
        exception.Message));
    }
  }
}
