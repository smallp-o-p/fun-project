using System;
using System.Collections.Generic;
using System.Linq;

namespace FunProject.Battle;

public sealed class BattleActionExecutor
{
  private readonly BattleSession _session;
  private readonly LinkedList<BattleAction> _pending = [];
  private readonly BattleTriggerRegistry _triggerRegistry = new();
  private readonly SysColGeneric.HashSet<BattleAction> _started = [];

  public int PendingCount => _pending.Count;
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

      currentAction.NextAction(_session)
        .IfSome((sub) =>
        {
          BattleActionResult result = new Func<BattleActionResult>(() =>
          {
            try
            {
              // The submitted action is the unit of the start/complete contract;
              // per-step detail flows through the committed BattleEvent stream.
              if (_started.Add(currentAction))
                OnActionStart.Invoke(currentAction);
              return ExecuteQueuedAction(sub, currentAction);
            }
            catch (Exception exception)
            {
              var res = BattleActionResult.Failure(
               sub,
               BattleActionFailureReason.UnexpectedError,
               exception.Message);
              ConsumeResult(sub, currentAction, res);
              return res;
            }
          })();

          ReportResult(result, sub, currentAction)
            .IfSome(reportedResult =>
            {
              _started.Remove(currentAction);
              LastResult = Some(reportedResult);
              OnActionComplete.Invoke(reportedResult);
              results.Add(reportedResult);
            });
        });
    }

    return results;
  }

  private BattleActionResult ExecuteQueuedAction(BattleAction action, BattleAction activeAction)
  {
    List<BattleEvent> committedEvents = [];

    // TODO: It's a little awkward to go back-and-forth with the session to get reaction events.
    Action<BattleEvent> captureCommittedEvent = committedEvents.Add;
    BattleActionResult result;
    _session.BattleEventCommitted += captureCommittedEvent;

    try
    {
      result = action.Execute(_session);
    }
    finally
    {
      _session.BattleEventCommitted -= captureCommittedEvent;
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

    return result.Succeeded
      ? Some(BattleActionResult.Success(
        activeAction,
        result.AffectedUnit,
        result.Message))
      : Some(BattleActionResult.Failure(
        activeAction,
        result.FailureReason,
        result.Message));
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
