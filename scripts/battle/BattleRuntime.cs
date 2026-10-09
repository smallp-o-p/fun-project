using System;

namespace FunProject.Battle;

public sealed class BattleRuntime : IDisposable
{
  private readonly BattleSession _session;
  private readonly BattleActionExecutor _actions;
  private bool _disposed;
  private bool _executing;
  private bool _executionFaulted;
  private EnemyTurnController? _enemyTurns;

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
    if (_enemyTurns is not null) throw new InvalidOperationException("Enemy turns are already enabled.");
    if (_executionFaulted) throw new InvalidOperationException("Cannot enable enemy turns after an execution fault.");
    var player = _session.PlayerFaction.Match(value => value,
      () => throw new InvalidOperationException("Enemy turns require a designated player faction."));
    EnterExecution();
    try
    {
      _enemyTurns = new(this, player, new EnemyPolicy(settings), _session.GlobalFactionTurnOrder.Count, ExecuteSingle);
      _enemyTurns.Drain();
    }
    catch
    {
      StopAfterFault();
      throw;
    }
    finally { _executing = false; }
  }

  // Diagnostics remain detached and inspectable after disposal.
  public System.Collections.Generic.IReadOnlyList<EnemyDecisionTrace> LatestEnemyDecisions
    => _enemyTurns?.LatestDecisions ?? [];
  public string EnemyTurnFallbackReason => _enemyTurns?.FallbackReason ?? "";
  internal bool IsDisposed => _disposed;

  public BattleActionExecResult ExecuteAction(BattleAction action)
  {
    EnterExecution();
    try
    {
      var result = ExecuteSingle(action);
      _enemyTurns?.Drain();
      return result;
    }
    catch
    {
      StopAfterFault();
      throw;
    }
    finally { _executing = false; }
  }

  // Both public and automated submissions use this path. The outer execution guard spans
  // observers and the complete drain, so a callback cannot nest a new submission.
  private BattleActionExecResult ExecuteSingle(BattleAction action)
  {
    ThrowIfDisposed();
    ArgumentNullException.ThrowIfNull(action);
    _actions.EnsureCanSubmit();
    ActionStarted.Invoke(action);
    ThrowIfDisposed();
    var result = _actions.Submit(action);
    ActionCompleted.Invoke(result);
    return result;
  }

  private void EnterExecution()
  {
    ThrowIfDisposed();
    if (_executing) throw new InvalidOperationException("Cannot submit or enable enemy turns during another execution.");
    _executing = true;
  }

  private void StopAfterFault()
  {
    _executionFaulted = true;
    _enemyTurns?.Stop("Execution fault");
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
    _enemyTurns?.Stop("Disposed");
    _session.BattleEventCommitted -= RaiseBattleEventCommitted;
    _actions.Dispose();
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
