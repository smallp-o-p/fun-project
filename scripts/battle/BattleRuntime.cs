using System;
using System.Threading;
using System.Threading.Tasks;

namespace FunProject.Battle;

public sealed class BattleRuntime : IDisposable
{
  private readonly BattleSession _session;
  private readonly BattleActionExecutor _actions;
  private readonly BattleAccessGuard _access = new();
  private readonly CancellationTokenSource _closing = new();
  private readonly CancellationToken _closingToken;
  private Task? _cancellation;
  private bool _disposed;

  public event Action<BattleEvent> BattleEventCommitted = delegate { };
  public event Action<BattleAction> ActionStarted = delegate { };
  public event Action<BattleActionExecResult> ActionCompleted = delegate { };
  public CancellationToken ClosingToken => _closingToken;
  public bool IsClosing => _access.IsClosing;

  internal BattleRuntime(BattleSession session)
  {
    ArgumentNullException.ThrowIfNull(session);
    _session = session;
    _closingToken = _closing.Token;
    _session.BattleEventCommitted += RaiseBattleEventCommitted;
    _access.TryMutation(execution: false);
    try
    {
      _session.BindBattleAccess(_access);
      _actions = new BattleActionExecutor(session);
    }
    catch
    {
      _session.BattleEventCommitted -= RaiseBattleEventCommitted;
      _access.RequestClose();
      _access.ExitMutation(execution: false);
      if (_access.TryEnterDisposal())
      {
        _access.UnbindSharedState();
        _access.FinishDisposal();
      }
      _closing.Dispose();
      throw;
    }
    finally
    {
      if (_access.IsOwned) _access.ExitMutation(execution: false);
    }
  }

  public Either<BattleAccessFailure, BattleQueryScope> TryAcquireQueryScope() => _access.TryRead();

  public Either<BattleAccessFailure, TResult> TryQuery<TResult>(IBattleSessionQuery<TResult> query)
  {
    ArgumentNullException.ThrowIfNull(query);
    return TryAcquireQueryScope().Map(scope =>
    {
      using (scope) return query.Execute(_session);
    });
  }

  public TResult Query<TResult>(IBattleSessionQuery<TResult> query) => Unwrap(TryQuery(query));

  private TResult Read<TResult>(Func<TResult> read)
  {
    using var scope = Unwrap(TryAcquireQueryScope());
    return read();
  }

  public Option<AliveUnit> TryGetAlive(BattleUnitState unit)
  {
    ArgumentNullException.ThrowIfNull(unit);
    return Read(() => _session.TryGetAlive(unit));
  }

  public Option<LiveObject> TryGetAliveObject(BattleObjectState obj)
  {
    ArgumentNullException.ThrowIfNull(obj);
    return Read(() => _session.TryGetAliveObject(obj));
  }

  public Option<AttackTarget> TryGetAttackTarget(BattleEntity entity)
  {
    ArgumentNullException.ThrowIfNull(entity);
    return Read(() => _session.TryGetAttackTarget(entity));
  }

  public Option<BattleBoardState.ValidatedPoint> TryGetTile(Vector3I coordinates)
    => Read(() => _session.Board.ValidatePoint(coordinates));

  public Either<BattleAccessFailure, BattleActionExecResult> TryExecuteAction(
    Func<BattleRuntime, BattleAction> createAction, Option<long> expectedRevision = default)
  {
    ArgumentNullException.ThrowIfNull(createAction);
    Option<BattleAccessFailure> failure = _access.TryMutation(execution: true);
    if (failure.IsSome) return Left(failure.IfNone(BattleAccessFailure.Closed));
    try
    {
      _actions.EnsureCanSubmit();
      if (expectedRevision.Match(revision => revision != _access.Revision, () => false))
        return Left(BattleAccessFailure.StaleRevision);
      _access.AdvanceRevision();
      BattleAction action = createAction(this);
      ArgumentNullException.ThrowIfNull(action);
      ActionStarted.Invoke(action);
      BattleActionExecResult result = _actions.Submit(action);
      // Executor cleanup is complete; ownership still protects callback reads.
      ActionCompleted.Invoke(result);
      return Right(result);
    }
    finally { _access.ExitMutation(execution: true); }
  }

  public BattleActionExecResult ExecuteAction(BattleAction action)
    => Unwrap(TryExecuteAction(_ => action));

  public Either<BattleAccessFailure, LanguageExt.Unit> TryRegisterHook<TEventKey>(BattleHook hook, int priority = 0)
    where TEventKey : BattleEventTag
  {
    Option<BattleAccessFailure> failure = _access.TryMutation(execution: false);
    if (failure.IsSome) return Left(failure.IfNone(BattleAccessFailure.Closed));
    try
    {
      _actions.RegisterHook<TEventKey>(hook, priority);
      if (!_access.IsExecuting) _access.AdvanceRevision();
      return Right(unit);
    }
    finally { _access.ExitMutation(execution: false); }
  }

  public Either<BattleAccessFailure, bool> TryUnregisterHook<TEventKey>(BattleHook hook)
    where TEventKey : BattleEventTag
  {
    Option<BattleAccessFailure> failure = _access.TryMutation(execution: false);
    if (failure.IsSome) return Left(failure.IfNone(BattleAccessFailure.Closed));
    try
    {
      bool changed = _actions.UnregisterHook<TEventKey>(hook);
      if (changed && !_access.IsExecuting) _access.AdvanceRevision();
      return Right(changed);
    }
    finally { _access.ExitMutation(execution: false); }
  }

  public void RegisterHook<TEventKey>(BattleHook hook, int priority = 0) where TEventKey : BattleEventTag
    => Unwrap(TryRegisterHook<TEventKey>(hook, priority));
  public bool UnregisterHook<TEventKey>(BattleHook hook) where TEventKey : BattleEventTag
    => Unwrap(TryUnregisterHook<TEventKey>(hook));

  public void RequestClose()
  {
    if (_access.RequestClose())
      Volatile.Write(ref _cancellation, _closing.CancelAsync());
  }

  public bool TryFinishDisposal()
  {
    _access.RequireHostThread();
    if (_disposed) return true;
    Task? cancellation = Volatile.Read(ref _cancellation);
    if (!IsClosing || cancellation is null || !cancellation.IsCompleted || !_access.TryEnterDisposal())
      return false;
    try
    {
      _session.BattleEventCommitted -= RaiseBattleEventCommitted;
      _actions.Dispose();
      _access.UnbindSharedState();
      _closing.Dispose();
      _disposed = true;
    }
    finally
    {
      if (_disposed) _access.FinishDisposal();
      else _access.ExitDisposal();
    }
    cancellation.GetAwaiter().GetResult();
    return true;
  }

  public void Dispose() { RequestClose(); TryFinishDisposal(); }
  internal long Revision { get { _access.RequireReadAccess(); return _access.Revision; } }
  internal void RequireReadAccess() => _access.RequireReadAccess();
  internal void RequireMutationAccess() => _access.RequireMutationAccess();

  private TResult Unwrap<TResult>(Either<BattleAccessFailure, TResult> result) => result.Match(
    Right: value => value,
    Left: failure => failure == BattleAccessFailure.Closed
      ? throw new ObjectDisposedException(nameof(BattleRuntime))
      : throw new InvalidOperationException($"Unexpected battle access failure: {failure}."));

  private void RaiseBattleEventCommitted(BattleEvent battleEvent) => BattleEventCommitted.Invoke(battleEvent);
}
