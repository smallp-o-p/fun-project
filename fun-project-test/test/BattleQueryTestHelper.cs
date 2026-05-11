using FunProject.Battle;

internal static class BattleQueryTestHelper
{
  public static Either<BattleQueryFailure, TResult> Query<TResult>(
    BattleSession session,
    BattleSessionQuery<TResult> query)
  {
    using var runtime = new BattleRuntime(session);
    return runtime.Query(query);
  }

  public static TResult GetValue<TResult>(Either<BattleQueryFailure, TResult> result)
  {
    return result.Match(
      failure => throw new InvalidOperationException(failure.Message),
      value => value);
  }

  public static BattleQueryFailure GetFailure<TResult>(Either<BattleQueryFailure, TResult> result)
  {
    return result.Match(
      failure => failure,
      _ => throw new InvalidOperationException("Expected query failure, but query succeeded."));
  }
}
