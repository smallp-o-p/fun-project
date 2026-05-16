using System;

namespace FunProject.Battle;

public enum BattleActionFailureReason
{
  None,
  Rejected,
  UnsupportedAction,
  UnexpectedError,
}

public readonly record struct BattleActionResult(
  BattleAction Action,
  bool Succeeded,
  BattleActionFailureReason FailureReason = BattleActionFailureReason.None,
  Option<BattleUnitState> AffectedUnit = default,
  string Message = "")
{
  public static BattleActionResult Success(BattleAction action)
  {
    return Success(action, None, "");
  }

  public static BattleActionResult Success(
    BattleAction action,
    BattleUnitState affectedUnit)
  {
    return Success(action, Some(affectedUnit), "");
  }

  public static BattleActionResult Success(
    BattleAction action,
    Option<BattleUnitState> affectedUnit,
    string message)
  {
    ArgumentNullException.ThrowIfNull(action);
    return new BattleActionResult(action, true, BattleActionFailureReason.None, affectedUnit, message);
  }

  public static BattleActionResult Failure(BattleAction action, BattleActionFailureReason failureReason, string message)
  {
    ArgumentNullException.ThrowIfNull(action);
    if (failureReason == BattleActionFailureReason.None)
      throw new ArgumentOutOfRangeException(nameof(failureReason), "Failed actions must specify a failure reason.");

    return new BattleActionResult(action, false, failureReason, None, message);
  }
}
