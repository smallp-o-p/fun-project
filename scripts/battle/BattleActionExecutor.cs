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

  public BattleActionExecutor(BattleSession session)
  {
    ArgumentNullException.ThrowIfNull(session);
    _session = session;
    _session.BattleEventCommitted += OnEventCommitted;

    // Status effects and armor regen run before conscious stun recovery at turn end.
    RegisterHook<TurnEndedBattleEvent>(new StatusEffectSystem(), priority: -100);
    RegisterHook<TurnEndedBattleEvent>(new ArmorRegenSystem(), priority: -100);
    RegisterHook<TurnEndedBattleEvent>(new StunRecoverySystem(), priority: -90);
    RegisterHook<ItemThrownBattleEvent>(new CapabilityEffectSystem());

    RegisterHook<TurnStartedBattleEvent>(new TurnStartBuffHook());
    RegisterHook<UnitAddedBattleEvent>(new UnitSpawnedBuffHook());

    // Objectives are an ordinary default system, receiving every event through the
    // BattleEventTag catch-all key and filtering their declared observed keys internally.
    RegisterHook<BattleEventTag>(new ObjectiveSystem(_session), priority: 100);
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

  public BattleActionExecResult Submit(BattleAction action)
  {
    ThrowIfDisposed();
    return DoAction(action);
  }

  private BattleActionExecResult DoAction(BattleAction action)
  {
    int logStart = _eventLog.Count;
    _pendingActions.Push(action);

    try
    {
      // A mid-dispatch battle end (objective directive, player-wipe backstop) drops all
      // remaining work: queued interrupts and un-executed composite steps must never run
      // against an ended session (their primitives require InProgress).
      while (_pendingActions.Count > 0 && _session.Phase != BattlePhase.Ended)
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

      if (_session.Phase == BattlePhase.Ended)
        _pendingActions.Clear();
    }
    catch
    {
      // A failed submission is fully unwound: nothing queued stays executable, so a later
      // Submit starts from a clean slate instead of resuming stale work.
      _pendingActions.Clear();
      throw;
    }
    finally
    {
      _inFlightAction = None;
      _capturedInterrupts.Clear();
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
