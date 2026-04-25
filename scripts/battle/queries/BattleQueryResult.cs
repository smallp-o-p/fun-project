using System;

namespace FunProject.Battle;

public abstract class BattleQueryResult<TResult>
{
  public string QueryId { get; }
  public abstract bool Succeeded { get; }

  protected BattleQueryResult(string queryId)
  {
    if (string.IsNullOrWhiteSpace(queryId))
      throw new ArgumentException("Query id cannot be null or whitespace.", nameof(queryId));

    QueryId = queryId;
  }

  public static BattleQueryResult<TResult> Success(string queryId, TResult value)
  {
    return new BattleQuerySuccess<TResult>(queryId, value);
  }

  public static BattleQueryResult<TResult> Failed(
    string queryId,
    BattleQueryFailureReason reason,
    string message)
  {
    return new BattleQueryFailureResult<TResult>(queryId, new BattleQueryFailure(reason, message));
  }

  public TMatched Match<TMatched>(
    Func<BattleQuerySuccess<TResult>, TMatched> onSuccess,
    Func<BattleQueryFailureResult<TResult>, TMatched> onFailure)
  {
    if (this is BattleQuerySuccess<TResult> success)
      return onSuccess(success);
    if (this is BattleQueryFailureResult<TResult> failure)
      return onFailure(failure);

    throw new InvalidOperationException($"Unsupported battle query result type {GetType().Name}.");
  }
}

public sealed class BattleQuerySuccess<TResult> : BattleQueryResult<TResult>
{
  public override bool Succeeded => true;
  public TResult Value { get; }

  public BattleQuerySuccess(string queryId, TResult value)
    : base(queryId)
  {
    Value = value;
  }
}

public sealed class BattleQueryFailureResult<TResult> : BattleQueryResult<TResult>
{
  public override bool Succeeded => false;
  public BattleQueryFailure Failure { get; }

  public BattleQueryFailureResult(string queryId, BattleQueryFailure failure)
    : base(queryId)
  {
    Failure = failure;
  }
}
