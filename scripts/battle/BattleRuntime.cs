using System;
using System.Collections.Generic;

namespace FunProject.Battle;

public sealed class BattleRuntime : IDisposable
{
  private readonly BattleSession _session;
  private readonly BattleActionExecutor _actions;
  private bool _disposed;
  public List<BattleAction> ActionLog = [];
  public List<BattleEvent> EventLog = [];

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
    _actions = new BattleActionExecutor(session);

    _session.BattleEventCommitted += RaiseBattleEventCommitted;
    _actions.OnActionStart += RaiseActionStarted;
    _actions.OnActionComplete += RaiseActionCompleted;
  }

  public Either<BattleQueryFailure, TResult> Query<TResult>(BattleSessionQuery<TResult> query)
  {
    ThrowIfDisposed();
    ArgumentNullException.ThrowIfNull(query);
    return query.Execute(_session);
  }

  public IReadOnlyList<BattleActionResult> ExecuteAction(BattleAction action)
  {
    ThrowIfDisposed();
    ActionLog.Add(action);
    return _actions.Submit(action);
  }

  public void RegisterTrigger<TEventKey>(BattleTrigger trigger)
    where TEventKey : BattleEventTag
  {
    ThrowIfDisposed();
    _actions.RegisterTrigger<TEventKey>(trigger);
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
    EventLog.Add(battleEvent);
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
