using System;

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
    _enemyController = new(this);
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

  public void EnableEnemyTurns(EnemyPolicySettings? settings = null)
  {
    ThrowIfDisposed();
    EnsureNotSubmitting();
    var player = _session.PlayerFaction.Match(value => value,
      () => throw new InvalidOperationException("Enemy turns require a designated player faction."));
    _enemyController.Enable(player, settings);
    _enemyController.ExecutePendingEnemyTurns();
  }

  public BattleActionExecResult ExecuteAction(BattleAction action) => ExecuteSingleAction(action);

  // Guard one synchronous submission, including its callbacks. This prevents observers
  // from submitting recursively; it does not lock or span the enemy-turn loop.
  internal BattleActionExecResult ExecuteSingleAction(BattleAction action)
  {
    ThrowIfDisposed();
    EnsureNotSubmitting();
    ArgumentNullException.ThrowIfNull(action);
    _actions.EnsureCanSubmit();
    _submitting = true;
    BattleActionExecResult result;
    try
    {
      ActionStarted.Invoke(action);
      ThrowIfDisposed();
      result = _actions.Submit(action);
      ActionCompleted.Invoke(result);
    }
    catch
    {
      _enemyController.Stop();
      throw;
    }
    finally { _submitting = false; }
    // Activation events only request execution. Settle it after every interrupt and
    // completion observer, with the submission guard released for ordinary AI actions.
    _enemyController.ExecutePendingEnemyTurns();
    return result;
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
  {
    if (battleEvent is ActiveSideChangedBattleEvent activation)
      _enemyController.OnFactionActivated(activation.Faction);
    BattleEventCommitted.Invoke(battleEvent);
  }
}
