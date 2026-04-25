using System;

namespace FunProject.Battle;

public sealed class BattleQueryFailure
{
  public BattleQueryFailureReason Reason { get; }
  public string Message { get; }

  public BattleQueryFailure(BattleQueryFailureReason reason, string message)
  {
    if (string.IsNullOrWhiteSpace(message))
      throw new ArgumentException("Query failure message cannot be null or whitespace.", nameof(message));

    Reason = reason;
    Message = message;
  }
}
