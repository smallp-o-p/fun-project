using System;
using FunProject.Combatants;

namespace FunProject.Battle;

public sealed class BattleRuntime : IDisposable
{
  private readonly BattleSession _session;
  private readonly BattleActionExecutor _actions;
  private bool _disposed;
  private bool _submitting;
  private readonly EnemyTurnController _enemyController;

  public event Action<BattleEvent> BattleEventCommitted = delegate { };
  public event Action<BattleAction> ActionStarted = delegate { };
  public event Action<BattleActionExecResult> ActionCompleted = delegate { };

  // Implementation detail: the factory is the only production assembly point. A runtime is
  // never an alternate public construction path.
  internal BattleRuntime(BattleSession session)
  {
    ArgumentNullException.ThrowIfNull(session);

    _session = session;
    _session.BattleEventCommitted += RaiseBattleEventCommitted;
    _actions = new BattleActionExecutor(session);
    _enemyController = new(this, new(200, 25, 100, 75));

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

  [InvalidatesProofs]
  public BattleActionExecResult ExecuteAction(BattleAction action)
  {
    var result = ExecuteSingleAction(action);
    ExecuteNonPlayerTurn();
    return result;
  }

  // Guard one synchronous submission, including its callbacks. This prevents observers
  // from submitting recursively; it does not lock or span the enemy-turn loop.
  [InvalidatesProofs]
  internal BattleActionExecResult ExecuteSingleAction(BattleAction action)
  {
    ThrowIfDisposed();
    EnsureNotSubmitting();
    ArgumentNullException.ThrowIfNull(action);
    _actions.EnsureCanSubmit();
    _submitting = true;
    try
    {
      ActionStarted.Invoke(action);
      ThrowIfDisposed();
      var result = _actions.Submit(action);
      ActionCompleted.Invoke(result);
      return result;
    }
    catch
    {
      _enemyController.Stop();
      throw;
    }
    finally { _submitting = false; }
  }

  private void ExecuteNonPlayerTurn()
  {
    if (_session.PlayerFaction.Case is not Faction player) return;
    while (!_disposed && _session.Outcome.IsNone && !_enemyController.IsStopped)
    {
      var active = _session.ActiveSide;
      if (ReferenceEquals(active, player)) return;
      _enemyController.ExecuteEnemyActions(active);
    }
  }

  private void EnsureNotSubmitting()
  {
    if (_submitting) throw new InvalidOperationException("Cannot submit or enable enemy turns from an action callback.");
  }

  public void RegisterHook<TEventKey>(BattleHook hook, int priority = 0)
    where TEventKey : IBattleEventTag
  {
    ThrowIfDisposed();
    _actions.RegisterHook<TEventKey>(hook, priority);
  }

  public bool UnregisterHook<TEventKey>(BattleHook hook)
    where TEventKey : IBattleEventTag
  {
    ThrowIfDisposed();
    return _actions.UnregisterHook<TEventKey>(hook);
  }

  [InvalidatesProofs]
  public void Dispose()
  {
    if (_disposed)
      return;

    _disposed = true;
    _enemyController.Stop();
    _session.BattleEventCommitted -= RaiseBattleEventCommitted;
    _actions.Dispose();
  }

  private void ThrowIfDisposed()
  {
    ObjectDisposedException.ThrowIf(_disposed, this);
  }

  private void RaiseBattleEventCommitted(BattleEvent battleEvent)
    => BattleEventCommitted.Invoke(battleEvent);
}
