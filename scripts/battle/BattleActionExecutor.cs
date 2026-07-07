using System;
using System.Collections.Generic;
using System.Linq;

namespace FunProject.Battle;

public sealed class BattleActionExecutor
{
  private readonly BattleSession _session;
  private readonly LinkedList<BattleAction> _pending = [];

  public Option<BattleActionResult> LastResult { get; private set; }

  public event Action<BattleAction> OnActionStart = delegate { };
  public event Action<BattleActionResult> OnActionComplete = delegate { };

  public BattleActionExecutor(BattleSession session)
  {
    ArgumentNullException.ThrowIfNull(session);
    _session = session;
  }

  public IReadOnlyList<BattleActionResult> Submit(BattleAction action)
  {
    if (_session.IsDispatchingEvents)
      throw new InvalidOperationException("Cannot submit actions while battle events are dispatching; hooks raise follow-up events through the session instead.");
    Enqueue(action);
    return Tick();
  }

  private void Enqueue(BattleAction action)
  {
    _pending.AddLast(action);
  }

  private IReadOnlyList<BattleActionResult> Tick()
  {
    List<BattleActionResult> results = [];

    while (_pending.Count > 0)
    {
      BattleAction currentAction = _pending.First!.Value;
      _pending.RemoveFirst();

      // Capture before NextAction may transition state away from Pending.
      bool isFirstStart = !currentAction.HasStarted;
      if (currentAction.NextAction(_session).Case is not BattleAction sub)
        continue;

      BattleActionResult result;
      try
      {
        // The submitted action is the unit of the start/complete contract;
        // per-step detail flows through the committed BattleEvent stream.
        if (isFirstStart)
          OnActionStart.Invoke(currentAction);
        result = ExecuteQueuedAction(sub, currentAction);
      }
      catch (Exception exception)
      {
        result = BattleActionResult.Failure(
          sub,
          BattleActionFailureReason.UnexpectedError,
          exception.Message);
        ConsumeResult(sub, currentAction, result);
      }

      ReportResult(result, sub, currentAction)
        .IfSome(reportedResult =>
        {
          LastResult = Some(reportedResult);
          OnActionComplete.Invoke(reportedResult);
          results.Add(reportedResult);
        });
    }

    return results;
  }

  private BattleActionResult ExecuteQueuedAction(BattleAction action, BattleAction activeAction)
  {
    // The session opens an action window for this primitive's Execute: hooks firing during
    // the dispatches it causes may return interrupt actions, which accumulate in the window
    // (in evaluation order) alongside the in-flight action HookContext exposes.
    IReadOnlyList<BattleAction> interrupts;
    BattleActionResult result;
    _session.BeginActionExecution(action);

    try
    {
      result = action.Execute(_session);
    }
    finally
    {
      interrupts = _session.EndActionExecution();
    }

    ConsumeResult(action, activeAction, result);
    if (!result.Succeeded)
    {
      return result;
    }

    if (!activeAction.IsDone())
      _pending.AddFirst(activeAction);

    foreach (var reaction in interrupts.Reverse())
      _pending.AddFirst(reaction);

    return result;
  }

  private static Option<BattleActionResult> ReportResult(
    BattleActionResult result,
    BattleAction action,
    BattleAction activeAction)
  {
    if (ReferenceEquals(action, activeAction))
      return Some(result);

    if (!activeAction.IsDone())
      return None;

    return Some(result with { Action = activeAction });
  }

  private static void ConsumeResult(BattleAction primitiveAction, BattleAction activeAction, BattleActionResult result)
  {
    primitiveAction.ConsumeResult(result);
    if (!ReferenceEquals(primitiveAction, activeAction))
      activeAction.ConsumeResult(result);
  }

}
