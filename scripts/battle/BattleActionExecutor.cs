#nullable enable
using FunProject.Items;
using Godot;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace FunProject.Battle;

public enum BattleActionFailureReason
{
  None,
  UnsupportedAction,
  ActionRejected,
  UnexpectedError,
}

public readonly record struct BattleActionExecutionResult(
  BattleActionIntent Intent,
  bool Succeeded,
  BattleActionFailureReason FailureReason = BattleActionFailureReason.None,
  string? Message = null)
{
  public static BattleActionExecutionResult Success(BattleActionIntent intent, string? message = null)
  {
    return new BattleActionExecutionResult(intent, true, BattleActionFailureReason.None, message);
  }

  public static BattleActionExecutionResult Failure(BattleActionIntent intent, BattleActionFailureReason failureReason, string? message = null)
  {
    if (failureReason == BattleActionFailureReason.None)
      throw new ArgumentOutOfRangeException(nameof(failureReason), "Failed results must specify a failure reason.");

    return new BattleActionExecutionResult(intent, false, failureReason, message);
  }
}

public sealed class BattleActionExecutor
{
  private readonly BattleSession _session;
  private readonly Queue<BattleActionIntent> _pending = [];

  public int PendingCount => _pending.Count;
  public bool IsBusy { get; private set; }
  public BattleActionIntent? ActiveIntent { get; private set; }
  public BattleActionExecutionResult? LastResult { get; private set; }

  public event Action<BattleActionIntent>? ActionStarted;
  public event Action<BattleActionExecutionResult>? ActionResolved;

  public BattleActionExecutor([NotNull] BattleSession session)
  {
    _session = session;
  }

  public void Enqueue([NotNull] BattleActionIntent intent)
  {
    _pending.Enqueue(intent);
  }

  public void EnqueueRange([NotNull] IEnumerable<BattleActionIntent> intents)
  {
    foreach (var intent in intents)
      Enqueue(intent);
  }

  public BattleActionExecutionResult? Tick()
  {
    if (IsBusy || _pending.Count == 0)
      return null;

    var activeIntent = _pending.Dequeue();
    ActiveIntent = activeIntent;
    IsBusy = true;

    BattleActionExecutionResult result;
    try
    {
      ActionStarted?.Invoke(activeIntent);
      result = Resolve(activeIntent);
    }
    catch (Exception exception)
    {
      result = BattleActionExecutionResult.Failure(
        activeIntent,
        BattleActionFailureReason.UnexpectedError,
        exception.Message);
    }

    LastResult = result;
    ActiveIntent = null;
    IsBusy = false;
    ActionResolved?.Invoke(result);
    return result;
  }

  public IReadOnlyList<BattleActionExecutionResult> DrainQueue(int maxActions = int.MaxValue)
  {
    if (maxActions <= 0)
      throw new ArgumentOutOfRangeException(nameof(maxActions));

    List<BattleActionExecutionResult> results = [];
    while (_pending.Count > 0 && results.Count < maxActions)
    {
      var result = Tick();
      if (!result.HasValue)
        break;

      results.Add(result.Value);
    }

    return results;
  }

  private BattleActionExecutionResult Resolve(BattleActionIntent intent)
  {
    return intent switch
    {
      MoveStepBattleActionIntent moveIntent => ResolveMoveStep(moveIntent),
      PassUnitBattleActionIntent passIntent => ResolvePassUnit(passIntent),
      EndFactionTurnBattleActionIntent endFactionTurnIntent => ResolveEndFactionTurn(endFactionTurnIntent),
      ThrowItemBattleActionIntent throwItemIntent => ResolveThrowItem(throwItemIntent),
      CustomBattleActionIntent customIntent => ResolveCustom(customIntent),
      _ => BattleActionExecutionResult.Failure(
        intent,
        BattleActionFailureReason.UnsupportedAction,
        $"Unsupported action intent '{intent.ActionId}'."),
    };
  }

  private BattleActionExecutionResult ResolveMoveStep(MoveStepBattleActionIntent intent)
  {
    var moved = _session.TryMoveUnitStep(intent.UnitId, intent.TargetCell, intent.ActionPointCost);
    return moved
      ? BattleActionExecutionResult.Success(intent)
      : BattleActionExecutionResult.Failure(intent, BattleActionFailureReason.ActionRejected, "Move step was rejected by the battle session.");
  }

  private BattleActionExecutionResult ResolvePassUnit(PassUnitBattleActionIntent intent)
  {
    var passed = _session.TryPassUnit(intent.UnitId);
    return passed
      ? BattleActionExecutionResult.Success(intent)
      : BattleActionExecutionResult.Failure(intent, BattleActionFailureReason.ActionRejected, "Pass unit action was rejected by the battle session.");
  }

  private BattleActionExecutionResult ResolveEndFactionTurn(EndFactionTurnBattleActionIntent intent)
  {
    var endedTurn = _session.TryEndFactionTurn(intent.IssuingSide);
    return endedTurn
      ? BattleActionExecutionResult.Success(intent)
      : BattleActionExecutionResult.Failure(intent, BattleActionFailureReason.ActionRejected, "End faction turn action was rejected by the battle session.");
  }

  private BattleActionExecutionResult ResolveThrowItem(ThrowItemBattleActionIntent intent)
  {
    var threwItem = _session.TryThrowItem(intent.UnitId, intent.Item, intent.TargetCell);
    return threwItem
      ? BattleActionExecutionResult.Success(intent)
      : BattleActionExecutionResult.Failure(intent, BattleActionFailureReason.ActionRejected, "Throw item action was rejected by the battle session.");
  }

  private BattleActionExecutionResult ResolveCustom(CustomBattleActionIntent intent)
  {
    var resolved = intent.Resolve(_session);
    return resolved
      ? BattleActionExecutionResult.Success(intent)
      : BattleActionExecutionResult.Failure(intent, BattleActionFailureReason.ActionRejected, "Custom action resolver rejected the action.");
  }
}
