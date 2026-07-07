using System;
using System.Collections.Generic;

namespace FunProject.Battle;

public sealed class BattleRuntime : IDisposable
{
  private readonly BattleSession _session;
  private readonly BattleActionExecutor _actions;
  private bool _disposed;

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

  public TResult Query<TResult>(IBattleSessionQuery<TResult> query)
  {
    ThrowIfDisposed();
    ArgumentNullException.ThrowIfNull(query);
    return query.Execute(_session);
  }

  // The interaction door for scene code holding a raw BattleUnitState: mints an aliveness proof
  // (Some iff the unit is alive in this session). The None path is what used to surface as a
  // query Left for a dead/foreign unit.
  public Option<AliveUnit> TryGetAlive(BattleUnitState unit)
  {
    ThrowIfDisposed();
    ArgumentNullException.ThrowIfNull(unit);
    return _session.TryGetAlive(unit);
  }

  // The tile mint door for scene code holding a raw coordinate (Some iff the tile is on this
  // session's board). In-bounds is a stable fact, so this proof cannot go stale.
  public Option<BattleBoardState.ValidatedPoint> TryGetTile(Godot.Vector3I coordinates)
  {
    ThrowIfDisposed();
    return _session.Board.ValidatePoint(coordinates);
  }

  public IReadOnlyList<BattleActionResult> ExecuteAction(BattleAction action)
  {
    ThrowIfDisposed();
    return _actions.Submit(action);
  }

  public void RegisterHook<TEventKey>(BattleHook hook, HookPhase phase, int priority = 0)
    where TEventKey : BattleEventTag
  {
    ThrowIfDisposed();
    _session.RegisterHook<TEventKey>(hook, phase, priority);
  }

  public bool UnregisterHook<TEventKey>(BattleHook hook, HookPhase phase)
    where TEventKey : BattleEventTag
  {
    ThrowIfDisposed();
    return _session.UnregisterHook<TEventKey>(hook, phase);
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
