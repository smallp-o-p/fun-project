using System;
using System.Collections.Generic;
using System.Linq;

namespace FunProject.Battle;

public sealed class BattleActionExecutor
{
  private readonly BattleSession _session;
  private readonly LinkedList<BattleAction> _pending = [];
  private readonly BattleTriggerRegistry _triggerRegistry = new();

  public Option<BattleActionResult> LastResult { get; private set; }

  public event Action<BattleAction> OnActionStart = delegate { };
  public event Action<BattleActionResult> OnActionComplete = delegate { };

  public BattleActionExecutor(BattleSession session)
  {
    ArgumentNullException.ThrowIfNull(session);
    _session = session;
  }

  public void RegisterTrigger<TEventKey>(BattleTrigger trigger)
    where TEventKey : BattleEventTag
  {
    _triggerRegistry.Register<TEventKey>(trigger);
  }

  public IReadOnlyList<BattleActionResult> Submit(BattleAction action)
  {
    if (_session.IsDispatchingEvents)
      throw new InvalidOperationException("Cannot submit actions while battle events are dispatching; listeners raise follow-up events through the session instead.");
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
    // The session records the events this Execute commits into a reused buffer, so we read
    // exactly this primitive's committed events — in commit order — without subscribing a
    // closure to the public BattleEventCommitted broadcast or allocating a List per step.
    IReadOnlyList<BattleEvent> committedEvents;
    BattleActionResult result;
    _session.BeginCommittedEventCapture();

    try
    {
      result = action.Execute(_session);
    }
    finally
    {
      committedEvents = _session.EndCommittedEventCapture();
    }

    ConsumeResult(action, activeAction, result);
    if (!result.Succeeded)
    {
      return result;
    }

    if (!activeAction.IsDone())
      _pending.AddFirst(activeAction);

    foreach (var reaction in EvaluateTriggerInterruptActions(action, committedEvents).Reverse())
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

  private IReadOnlyList<BattleAction> EvaluateTriggerInterruptActions(BattleAction sourceAction, IReadOnlyList<BattleEvent> committedEvents)
  {
    List<BattleAction> interruptActions = new()
    {
      Capacity = committedEvents.Count
    };

    foreach (BattleEvent battleEvent in committedEvents)
    {
      interruptActions.AddRange(_triggerRegistry.EvaluateInterruptActions(
        _session,
        battleEvent,
        sourceAction));
    }

    return interruptActions;
  }
}
