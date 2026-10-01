using System;

namespace FunProject.Battle;

/// <summary>
/// The stable battle facade: owns one internal running-or-completed representation for the
/// runtime's whole lifetime. Lifecycle-dependent reads go through <see cref="Query{T}"/>
/// with total queries (<see cref="GetCurrentTurnQuery"/>, <see cref="GetCompletedBattleQuery"/>);
/// gameplay submissions run only while running. Construction and opening dispatch belong to
/// the factory owner; presentation and ordinary callers never retain or construct the
/// running receiver.
/// </summary>
public sealed class BattleRuntime : IDisposable
{
  // The single lifecycle representation: running combat, or the frozen result installed at
  // settlement. Both total queries derive their answers from this one value.
  private abstract record Lifecycle
  {
    internal sealed record Running : Lifecycle;

    internal sealed record Completed(CompletedBattle Result) : Lifecycle;
  }

  private readonly BattleSession _session;
  private readonly BattleActionExecutor _actions;
  private Lifecycle _lifecycle = new Lifecycle.Running();
  private bool _disposed;

  public event Action<BattleEvent> BattleEventCommitted = delegate { };
  public event Action<BattleAction> ActionStarted = delegate { };
  public event Action<BattleActionExecResult> ActionCompleted = delegate { };

  // Engine-owned construction: the factory mints the runtime from a prepared state and
  // dispatches the opening turn; there is one session and one executor per runtime.
  internal BattleRuntime(BattleSession session)
  {
    ArgumentNullException.ThrowIfNull(session);

    _session = session;
    _session.BattleEventCommitted += RaiseBattleEventCommitted;
    _actions = new BattleActionExecutor(this);
    _session.ActionOptions.InstallContextProvider(GetReadContext);
  }

  internal BattleSession Session => _session;

  // Factory-owned construction door: builds the valid scheduler from the complete prepared
  // state and the running receiver around it.
  internal static BattleRuntime Create(BattleState preparedState)
  {
    ArgumentNullException.ThrowIfNull(preparedState);
    var scheduler = new TurnScheduler(
      preparedState.Factions,
      preparedState.HasConsciousUnits,
      preparedState.GetFactionConsciousUnits);
    return new BattleRuntime(new BattleSession(preparedState, scheduler));
  }

  // Factory-owned dispatch door: registers are done; the opening step reuses the ordinary
  // primitive orchestration (session-start and turn-start events, then the all-unit AP
  // top-up), so a terminal opening-turn objective settles through the normal path.
  internal void DispatchOpeningTurn()
  {
    ThrowIfDisposed();
    _actions.Execute(new OpeningTurn());
  }

  private sealed class OpeningTurn : BattleAction
  {
    public override Result Execute(BattleSession session)
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
    _session.ActionOptions.InvalidateAll();
  }

  internal BattleReadContext GetReadContext() => _lifecycle switch
  {
    Lifecycle.Completed completed => new BattleReadContext(_session.State, None, Some(completed.Result), None),
    _ => new BattleReadContext(_session.State, Some(_session.CurrentTurn), None, Some(_session)),
  };

  public TResult Query<TResult>(IBattleSessionQuery<TResult> query)
  {
    ThrowIfDisposed();
    ArgumentNullException.ThrowIfNull(query);
    return query.Execute(GetReadContext());
  }

  // The interaction door for scene code holding a raw BattleUnitState: mints an aliveness proof
  // (Some iff the unit is alive in this battle). The None path is what used to surface as a
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
  // currently targetable in this battle. The proof is a snapshot that may go stale across a
  // commit, like every other mint here.
  public Option<AttackTarget> TryGetAttackTarget(BattleEntity entity)
  {
    ThrowIfDisposed();
    ArgumentNullException.ThrowIfNull(entity);
    return _session.TryGetAttackTarget(entity);
  }

  // The tile mint door for scene code holding a raw coordinate (Some iff the tile is on this
  // battle's board). In-bounds is a stable fact, so this proof cannot go stale.
  public Option<BattleBoardState.ValidatedPoint> TryGetTile(Vector3I coordinates)
  {
    ThrowIfDisposed();
    return _session.State.Board.ValidatePoint(coordinates);
  }

  // Total facade door: Some means the submission actually ran; None means the battle was
  // already completed — nothing mutates and neither lifecycle signal fires.
  public Option<BattleActionExecResult> ExecuteAction(BattleAction action)
  {
    ThrowIfDisposed();
    ArgumentNullException.ThrowIfNull(action);
    if (_lifecycle is not Lifecycle.Running)
      return None;

    ActionStarted.Invoke(action);
    BattleActionExecResult result = _actions.Execute(action);
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
