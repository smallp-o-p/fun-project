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
  private readonly BattleSession _session;
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
  private bool _disposed;

  // Implementation detail: the factory constructs the session's sole executor. A second
  // executor would double-register the default systems.
  internal BattleActionExecutor(BattleSession session)
  {
    ArgumentNullException.ThrowIfNull(session);
    _session = session;
    _session.ActionOptions.InvalidateAll();
    _session.BattleEventCommitted += OnEventCommitted;

    // Status effects and armor regen run before conscious stun recovery at turn end.
    RegisterHook<TurnEndedBattleEvent>(new StatusEffectSystem(), priority: -100);
    RegisterHook<TurnEndedBattleEvent>(new ArmorRegenSystem(), priority: -100);
    RegisterHook<TurnEndedBattleEvent>(new StunRecoverySystem(), priority: -90);
    RegisterHook<ItemThrownBattleEvent>(new CapabilityEffectSystem());

    RegisterHook<TurnStartedBattleEvent>(new TurnStartBuffHook());
    RegisterHook<UnitAddedBattleEvent>(new UnitSpawnedBuffHook());

    // Objectives are an ordinary default system, receiving every event through the
    // IBattleEventTag catch-all key and filtering their declared observed keys internally.
    RegisterHook<IBattleEventTag>(new ObjectiveSystem(_session), priority: 100);
  }

  private void OnEventCommitted(BattleEvent battleEvent)
  {
    _eventLog.Add(battleEvent);

    var interrupts = _hooks.Fire(battleEvent, new HookContext(_session, _inFlightAction));
    if (interrupts.Count == 0)
      return;
    if (_inFlightAction.IsNone)
      throw new InvalidOperationException(
        "A hook returned interrupt actions while no executor action was in flight.");
    _capturedInterrupts.AddRange(interrupts);
  }

  public void RegisterHook<TEventKey>(BattleHook hook, int priority = 0)
    where TEventKey : IBattleEventTag
  {
    ThrowIfDisposed();
    _hooks.Register<TEventKey>(hook, priority);
  }

  public bool UnregisterHook<TEventKey>(BattleHook hook)
    where TEventKey : IBattleEventTag
  {
    ThrowIfDisposed();
    return _hooks.Unregister<TEventKey>(hook);
  }

  public BattleActionExecResult Submit(BattleAction action)
  {
    EnsureCanSubmit();
    return DoAction(action);
  }

  // One admission invariant shared by every submission door: a completed battle rejects new
  // work as a caller error, before any signal, queue mutation, or event. Disposal first.
  internal void EnsureCanSubmit()
  {
    ThrowIfDisposed();
    if (_session.Outcome.IsSome)
      throw new InvalidOperationException("Cannot submit actions to a completed battle.");
  }

  private BattleActionExecResult DoAction(BattleAction action)
  {
    int logStart = _eventLog.Count;
    _pendingActions.Push(action);
    _session.ActionOptions.BeginExecution();

    try
    {
      // A mid-dispatch battle end (objective directive, player-wipe backstop) drops all
      // remaining work: queued interrupts and un-executed composite steps must never run
      // against a battle whose outcome is already decided. New submissions are rejected by
      // EnsureCanSubmit; this guard only stops work in flight.
      while (_pendingActions.Count > 0 && _session.Outcome.IsNone)
      {
        var currentAction = _pendingActions.Peek();

        _capturedInterrupts.Clear();
        _inFlightAction = Some(currentAction);
        var actionState = currentAction.Execute(_session);

        switch (actionState)
        {
          case BattleAction.Result.Completed:
            {
              _pendingActions.Pop();
              foreach (var reaction in _capturedInterrupts.AsValueEnumerable().Reverse())
                _pendingActions.Push(reaction);
              break;
            }
          case BattleAction.Result.Interrupted:
            {
              _pendingActions.Pop();
              break;
            }
          case BattleAction.Result.Incomplete:
            {
              foreach (var reaction in _capturedInterrupts.AsValueEnumerable().Reverse())
                _pendingActions.Push(reaction);
              break;
            }
          case BattleAction.Result.Rejected:
            {
              _pendingActions.Pop();
              throw new InvalidOperationException("Action's parameters should have been verified.");
            }
        }
      }

      if (_session.Outcome.IsSome)
        _pendingActions.Clear();
    }
    catch
    {
      // A failed submission is fully unwound: nothing queued stays executable, so a later
      // Submit starts from a clean slate instead of resuming stale work.
      _pendingActions.Clear();
      _session.ActionOptions.InvalidateAll();
      throw;
    }
    finally
    {
      _inFlightAction = None;
      _capturedInterrupts.Clear();
      _session.ActionOptions.EndExecution();
    }

    return new BattleActionExecResult(action, _eventLog, logStart, _eventLog.Count - logStart);
  }

  public void Dispose()
  {
    if (_disposed)
      return;

    _session.BattleEventCommitted -= OnEventCommitted;
    _disposed = true;
  }

  private void ThrowIfDisposed()
  {
    ObjectDisposedException.ThrowIf(_disposed, this);
  }
}
