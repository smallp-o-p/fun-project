using FunProject.Battle;
using System;

internal static class BattleQueryTestHelper
{
  public static TResult GetValue<TResult>(BattleQueryResult<TResult> result)
  {
    ArgumentNullException.ThrowIfNull(result);

    if (result is BattleQuerySuccess<TResult> success)
      return success.Value;
    if (result is BattleQueryFailureResult<TResult> failure)
      throw new InvalidOperationException(failure.Failure.Message);

    throw new InvalidOperationException($"Unsupported battle query result type {result.GetType().Name}.");
  }

  public static BattleQueryFailure GetFailure<TResult>(BattleQueryResult<TResult> result)
  {
    ArgumentNullException.ThrowIfNull(result);

    if (result is BattleQueryFailureResult<TResult> failure)
      return failure.Failure;
    if (result is BattleQuerySuccess<TResult>)
      throw new InvalidOperationException("Expected query failure, but query succeeded.");

    throw new InvalidOperationException($"Unsupported battle query result type {result.GetType().Name}.");
  }
}
