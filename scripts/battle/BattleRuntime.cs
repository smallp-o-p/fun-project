using System;

namespace FunProject.Battle;

public sealed class BattleRuntime : IDisposable
{
  private readonly BattleSession _session;
  private readonly BattleActionExecutor _actions;
  private bool _disposed;

  public event Action<BattleEvent> BattleEventCommitted = delegate { };
  public event Action<BattleAction> ActionStarted = delegate { };
  public event Action<BattleActionExecResult> ActionCompleted = delegate { };

  public BattleRuntime(BattleSession session)
  {
    ArgumentNullException.ThrowIfNull(session);

    _session = session;
    _session.BattleEventCommitted += RaiseBattleEventCommitted;
    _actions = new BattleActionExecutor(session);
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

  public Option<LiveObject> TryGetAliveObject(BattleObjectState obj)
  {
    ThrowIfDisposed();
    ArgumentNullException.ThrowIfNull(obj);
    return _session.TryGetAliveObject(obj);
  }

  // The attack-target mint door for scene code holding a raw identity: Some iff the entity is
  // currently targetable in this session. The proof is a snapshot that may go stale across a
  // commit, like every other mint here.
  public Option<AttackTarget> TryGetAttackTarget(BattleEntity entity)
  {
    ThrowIfDisposed();
    ArgumentNullException.ThrowIfNull(entity);
    return _session.TryGetAttackTarget(entity);
  }

  // The tile mint door for scene code holding a raw coordinate (Some iff the tile is on this
  // session's board). In-bounds is a stable fact, so this proof cannot go stale.
  public Option<BattleBoardState.ValidatedPoint> TryGetTile(Vector3I coordinates)
  {
    ThrowIfDisposed();
    return _session.Board.ValidatePoint(coordinates);
  }

  public BattleActionExecResult ExecuteAction(BattleAction action)
  {
    ThrowIfDisposed();
    ActionStarted.Invoke(action);
    BattleActionExecResult result = _actions.Submit(action);
    ActionCompleted.Invoke(result);
    return result;
  }

  public void RegisterHook<TEventKey>(BattleHook hook, int priority = 0)
    where TEventKey : BattleEventTag
  {
    ThrowIfDisposed();
    _actions.RegisterHook<TEventKey>(hook, priority);
  }

  public bool UnregisterHook<TEventKey>(BattleHook hook)
    where TEventKey : BattleEventTag
  {
    ThrowIfDisposed();
    return _actions.UnregisterHook<TEventKey>(hook);
  }

  public void Dispose()
  {
    if (_disposed)
      return;

    _session.BattleEventCommitted -= RaiseBattleEventCommitted;
    _actions.Dispose();
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
}
