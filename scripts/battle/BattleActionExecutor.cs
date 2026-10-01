using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace FunProject.Battle;

public readonly struct BattleActionExecResult(BattleAction action, List<BattleEvent> log, int start, int count)
{
  public BattleAction Action { get; } = action;
  public ReadOnlySpan<BattleEvent> EventsThatOccurred => CollectionsMarshal.AsSpan(log).Slice(start, count);
}

/// <summary>
/// The "Command Executor" in the Command design pattern.
/// It handles execution of submitted BattleActions and is usually the main driver of mutation in BattleSession.
/// Also handles BattleHooks and interrupt machinery.
/// Submit Action --> Get events that occurred
/// </summary>
public sealed class BattleActionExecutor : IDisposable
{
  private readonly BattleRuntime _runtime;
  private readonly BattleHookRegistry _hooks = new();

  /// <summary>
  /// Actions to execute. Empty until an action is submitted, in which case we execute it, and stack any interrupt
  /// actions on top, and also execute those.
  /// </summary>
  private readonly Stack<BattleAction> _pendingActions = [];

  /// <summary>
  /// Constant, growing event log.
  /// </summary>
  private readonly List<BattleEvent> _eventLog = [];

  private readonly List<BattleAction> _capturedInterrupts = [];
  private Option<BattleAction> _inFlightAction = None;
  private bool _settling;
  private bool _disposed;

  // Engine-owned construction: the runtime mints its executor; there is one per runtime.
  // The executor owns the committed stream from the runtime's lifetime state.
  internal BattleActionExecutor(BattleRuntime runtime)
  {
    ArgumentNullException.ThrowIfNull(runtime);
    _runtime = runtime;
    _runtime.State.ActionOptions.InvalidateAll();
    _runtime.State.Committed += OnEventCommitted;

    // Status effects and armor regen run before conscious stun recovery at turn end.
    RegisterHook<TurnEndedBattleEvent>(new StatusEffectSystem(), priority: -100);
    RegisterHook<TurnEndedBattleEvent>(new ArmorRegenSystem(), priority: -100);
    RegisterHook<TurnEndedBattleEvent>(new StunRecoverySystem(), priority: -90);
    RegisterHook<ItemThrownBattleEvent>(new CapabilityEffectSystem());

    RegisterHook<TurnStartedBattleEvent>(new TurnStartBuffHook());
    RegisterHook<UnitAddedBattleEvent>(new UnitSpawnedBuffHook());

    // Objectives are an ordinary default system, receiving every event through the
    // BattleEventTag catch-all key and filtering their declared observed keys internally.
    RegisterHook<BattleEventTag>(new ObjectiveSystem(), priority: 100);
  }

  // The single committed-stream handler: the cause enters the append-only log FIRST,
  // cached options invalidate before any observer inspects them, runtime subscribers are
  // forwarded SECOND, and hooks fire THIRD — hook follow-ups stay queued and linear.
  private void OnEventCommitted(BattleEvent battleEvent)
  {
    _eventLog.Add(battleEvent);
    _runtime.State.ActionOptions.Invalidate(battleEvent);
    _runtime.RaiseBattleEventCommitted(battleEvent);

    // The read context derives from the runtime per firing, so an event committed at or
    // after settlement observes the completed representation with no running receiver.
    var interrupts = _hooks.Fire(battleEvent, new HookContext(_runtime.GetReadContext(), _inFlightAction));
    if (interrupts.Count == 0)
      return;
    if (_inFlightAction.IsNone && !_settling)
      throw new InvalidOperationException(
        "A hook returned interrupt actions while no executor action was in flight.");
    _capturedInterrupts.AddRange(interrupts);
  }

  public void RegisterHook<TEventKey>(BattleHook hook, int priority = 0)
    where TEventKey : BattleEventTag
  {
    ThrowIfDisposed();
    _hooks.Register<TEventKey>(hook, priority);
  }

  public bool UnregisterHook<TEventKey>(BattleHook hook)
    where TEventKey : BattleEventTag
  {
    ThrowIfDisposed();
    return _hooks.Unregister<TEventKey>(hook);
  }

  internal BattleActionExecResult Execute(BattleAction action)
  {
    // The running receiver resolves into this local execution scope and is released when
    // the submission finishes; the round is captured from the same scope at settlement.
    BattleSession session = _runtime.CurrentSession;
    int logStart = _eventLog.Count;
    var step = new BattleStep();
    session.BeginStep(step);
    _pendingActions.Push(action);
    session.ActionOptions.BeginExecution();

    try
    {
      // A pending terminal decision finishes the current primitive's synchronous work and
      // then stops the loop: queued interrupts and un-executed composite steps are separate
      // actions, not synchronous effects belonging to the settled step.
      while (_pendingActions.Count > 0 && !step.HasPendingOutcome && _runtime.IsRunning)
      {
        var currentAction = _pendingActions.Peek();

        _capturedInterrupts.Clear();
        _inFlightAction = Some(currentAction);
        var actionState = currentAction.Execute(session);

        switch (actionState)
        {
          case BattleAction.Result.Completed:
            {
              _pendingActions.Pop();
              PushCapturedInterrupts(step);
              break;
            }
          case BattleAction.Result.Interrupted:
            {
              _pendingActions.Pop();
              break;
            }
          case BattleAction.Result.Incomplete:
            {
              PushCapturedInterrupts(step);
              break;
            }
          case BattleAction.Result.Rejected:
            {
              _pendingActions.Pop();
              throw new InvalidOperationException("Action's parameters should have been verified.");
            }
        }
      }

      // Successful settlement happens only after the primitive returned and the synchronous
      // event queue drained: capture once, install Completed, broadcast exactly one end event.
      if (step.TryTakeOutcome(out BattleOutcome outcome))
        Settle(session, outcome);
    }
    catch
    {
      // A failed submission is fully unwound: nothing queued stays executable, so a later
      // Submit starts from a clean slate instead of resuming stale work.
      _pendingActions.Clear();
      session.ActionOptions.InvalidateAll();
      throw;
    }
    finally
    {
      _inFlightAction = None;
      _capturedInterrupts.Clear();
      session.ActionOptions.EndExecution();
      session.EndStep();
    }

    return new BattleActionExecResult(action, _eventLog, logStart, _eventLog.Count - logStart);
  }

  private void PushCapturedInterrupts(BattleStep step)
  {
    if (step.HasPendingOutcome)
      return;

    foreach (var reaction in _capturedInterrupts.AsValueEnumerable().Reverse())
      _pendingActions.Push(reaction);
  }

  private void Settle(BattleSession session, BattleOutcome outcome)
  {
    _settling = true;
    try
    {
      _pendingActions.Clear();
      _capturedInterrupts.Clear();
      _inFlightAction = None;

      CompletedBattle completed = CompletedBattle.Capture(session.State, outcome, session.RoundNumber);
      _runtime.InstallCompleted(completed);

      // End-event subscribers read frozen results and may record objective history; hook
      // follow-ups dispatch linearly, and any interrupts they return are discarded here.
      session.State.RaiseEvents(new SessionEndedBattleEvent(outcome));
    }
    finally
    {
      _settling = false;
      _pendingActions.Clear();
      _capturedInterrupts.Clear();
    }
  }

  public void Dispose()
  {
    if (_disposed)
      return;

    _runtime.State.Committed -= OnEventCommitted;
    _disposed = true;
  }

  private void ThrowIfDisposed()
  {
    ObjectDisposedException.ThrowIf(_disposed, this);
  }
}

// One execution scope per executor submission. The first accepted outcome wins and stays
// pending until settlement; a later effect in the same step cannot override it.
internal sealed class BattleStep
{
  private BattleOutcome? _outcome;

  internal bool HasPendingOutcome => _outcome is not null;

  internal void RequestEnd(BattleOutcome outcome) => _outcome ??= outcome;

  internal bool TryTakeOutcome(out BattleOutcome outcome)
  {
    if (_outcome is not BattleOutcome pending)
    {
      outcome = default;
      return false;
    }

    outcome = pending;
    return true;
  }
}
