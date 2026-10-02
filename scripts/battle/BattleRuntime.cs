using System;

namespace FunProject.Battle;

/// <summary>
/// The stable battle facade: owns one internal running-or-completed representation for the
/// runtime's whole lifetime; total lifecycle queries and gameplay submissions derive their
/// answers from it. Construction and opening dispatch belong to the factory owner.
/// </summary>
public sealed class BattleRuntime : IDisposable
{
  // The sole persistent owner of the running receiver or frozen result.
  private abstract record Lifecycle
  {
    internal sealed record Running(BattleSession Session) : Lifecycle;

    internal sealed record Completed(CompletedBattle Result) : Lifecycle;
  }

  private readonly BattleState _state;
  private readonly BattleActionExecutor _actions;
  private Lifecycle _lifecycle;
  private bool _disposed;

  public event Action<BattleEvent> BattleEventCommitted = delegate { };
  public event Action<BattleAction> ActionStarted = delegate { };
  public event Action<BattleActionExecResult> ActionCompleted = delegate { };

  private BattleRuntime(BattleState state)
  {
    ArgumentNullException.ThrowIfNull(state);

    _state = state;
    _lifecycle = new Lifecycle.Running(new BattleSession(state, new TurnScheduler(state)));
    _actions = new BattleActionExecutor(this);
    _state.ActionOptions.InstallContextProvider(GetReadContext);
  }

  // Resolves the current receiver and refuses after completion.
  internal BattleSession CurrentSession => _lifecycle switch
  {
    Lifecycle.Running running => running.Session,
    _ => throw new InvalidOperationException("The battle has no running receiver after completion."),
  };

  internal BattleState State => _state;

  internal static BattleRuntime Create(BattleState preparedState) => new(preparedState);

  internal void DispatchOpeningTurn()
  {
    ThrowIfDisposed();
    // The opening owns the same submission window as any Submit: declared-system callbacks
    // fire inside it, and a nested ExecuteAction from them must be rejected before touching
    // either queue. It stays internal orchestration — no public ActionStarted/ActionCompleted.
    _actions.BeginSubmission();
    try
    {
      _actions.Execute(new OpeningTurn());
    }
    finally
    {
      _actions.EndSubmission();
    }
  }

  private sealed class OpeningTurn : BattleAction
  {
    internal override Result ExecuteStep(BattleSession session)
    {
      session.OpeningTurnDispatch();
      return Result.Completed;
    }
  }

  internal bool IsRunning => _lifecycle is Lifecycle.Running;

  // Settlement boundary: installs the frozen report and dirties every retained option, so
  // completed reads derive from this representation and disabled options re-evaluate false.
  internal void InstallCompleted(CompletedBattle completed)
  {
    ArgumentNullException.ThrowIfNull(completed);
    _lifecycle = new Lifecycle.Completed(completed);
    _state.ActionOptions.InvalidateAll();
  }

  internal BattleReadContext GetReadContext() => _lifecycle switch
  {
    Lifecycle.Running running => new BattleReadContext(
      _state, Some(running.Session.CurrentTurn), None, Some(running.Session)),
    Lifecycle.Completed completed => new BattleReadContext(_state, None, Some(completed.Result), None),
    _ => throw new InvalidOperationException("Unknown lifecycle representation."),
  };

  public TResult Query<TResult>(IBattleSessionQuery<TResult> query)
  {
    ThrowIfDisposed();
    ArgumentNullException.ThrowIfNull(query);
    return query.Execute(GetReadContext());
  }

  // Proof mints use lifetime-owned state and remain usable after completion.
  public Option<AliveUnit> TryGetAlive(BattleUnitState unit)
  {
    ThrowIfDisposed();
    ArgumentNullException.ThrowIfNull(unit);
    return _state.TryGetAlive(unit);
  }

  public Option<LiveObject> TryGetAliveObject(BattleObjectState obj)
  {
    ThrowIfDisposed();
    ArgumentNullException.ThrowIfNull(obj);
    return _state.TryGetAliveObject(obj);
  }

  // The attack-target mint door for scene code holding a raw identity: Some iff the entity is
  // currently targetable in this battle. The proof is a snapshot that may go stale across a
  // commit, like every other mint here.
  public Option<AttackTarget> TryGetAttackTarget(BattleEntity entity)
  {
    ThrowIfDisposed();
    ArgumentNullException.ThrowIfNull(entity);
    return _state.TryGetAttackTarget(entity);
  }

  // The tile mint door for scene code holding a raw coordinate (Some iff the tile is on this
  // battle's board). In-bounds is a stable fact, so this proof cannot go stale.
  public Option<BattleBoardState.ValidatedPoint> TryGetTile(Vector3I coordinates)
  {
    ThrowIfDisposed();
    return _state.Board.ValidatePoint(coordinates);
  }

  // Total facade door: Some means the submission actually ran; None means the battle was
  // already completed — nothing mutates and neither lifecycle signal fires.
  public Option<BattleActionExecResult> ExecuteAction(BattleAction action)
  {
    ThrowIfDisposed();
    ArgumentNullException.ThrowIfNull(action);
    if (_lifecycle is not Lifecycle.Running)
      return None;

    // The submission window opens before ActionStarted and closes before ActionCompleted:
    // nested calls are rejected before touching either queue, while an ActionCompleted
    // observer may start a separate submission whose events stay outside this result's
    // bounded range. An observer fault after ActionCompleted begins is a notification
    // failure — the committed submission stands.
    _actions.BeginSubmission();
    BattleActionExecResult result;
    try
    {
      ActionStarted.Invoke(action);
      result = _actions.Execute(action);
    }
    finally
    {
      _actions.EndSubmission();
    }
    ActionCompleted.Invoke(result);
    return Some(result);
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

    _actions.Dispose();
    _disposed = true;
  }

  private void ThrowIfDisposed()
  {
    ObjectDisposedException.ThrowIf(_disposed, this);
  }

  internal void RaiseBattleEventCommitted(BattleEvent battleEvent)
  {
    BattleEventCommitted.Invoke(battleEvent);
  }
}
