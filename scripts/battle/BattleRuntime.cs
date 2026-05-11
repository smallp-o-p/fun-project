using System;
using System.Collections.Generic;

namespace FunProject.Battle;

public sealed class BattleRuntime : IDisposable
{
  private readonly BattleSession _session;
  private readonly BattleQueryRunner _queries;
  private readonly BattleActionExecutor _actions;
  private bool _disposed;

  public int PendingActionCount
  {
    get
    {
      ThrowIfDisposed();
      return _actions.PendingCount;
    }
  }

  public Option<BattleActionResult> LastActionResult
  {
    get
    {
      ThrowIfDisposed();
      return _actions.LastResult;
    }
  }

  public event Action<BattleEvent> BattleEventCommitted = delegate { };
  public event Action<BattleAction> ActionStarted = delegate { };
  public event Action<BattleActionResult> ActionCompleted = delegate { };

  public BattleRuntime(BattleSession session)
  {
    ArgumentNullException.ThrowIfNull(session);

    _session = session;
    _queries = new BattleQueryRunner(session);
    _actions = new BattleActionExecutor(session);

    _session.BattleEventCommitted += RaiseBattleEventCommitted;
    _actions.OnActionStart += RaiseActionStarted;
    _actions.OnActionComplete += RaiseActionCompleted;
  }

  public Either<BattleQueryFailure, TResult> Query<TResult>(BattleSessionQuery<TResult> query)
  {
    ThrowIfDisposed();
    return _queries.Execute(query);
  }

  public IReadOnlyList<BattleActionResult> ExecuteAction(BattleAction action)
  {
    ThrowIfDisposed();
    return _actions.Submit(action);
  }

  public void RegisterTrigger(BattleTrigger trigger, BattleEventType eventType)
  {
    ThrowIfDisposed();
    _actions.RegisterTrigger(trigger, eventType);
  }

  public void RegisterTrigger(BattleTrigger trigger, IEnumerable<BattleEventType> eventTypes)
  {
    ThrowIfDisposed();
    _actions.RegisterTrigger(trigger, eventTypes);
  }

  public void Dispose()
  {
    if (_disposed)
      return;

    _session.BattleEventCommitted -= RaiseBattleEventCommitted;
    _actions.OnActionStart -= RaiseActionStarted;
    _actions.OnActionComplete -= RaiseActionCompleted;
    _disposed = true;
  }

  private void ThrowIfDisposed()
  {
    ObjectDisposedException.ThrowIf(_disposed, this);
  }

  private void RaiseBattleEventCommitted(BattleEvent battleEvent)
  {
    BattleEventCommitted.Invoke(battleEvent);
  }

  private void RaiseActionStarted(BattleAction action)
  {
    ActionStarted.Invoke(action);
  }

  private void RaiseActionCompleted(BattleActionResult result)
  {
    ActionCompleted.Invoke(result);
  }
}
