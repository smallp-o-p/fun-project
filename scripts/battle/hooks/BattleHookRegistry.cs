using System;
using System.Collections.Generic;
using System.Linq;

namespace FunProject.Battle;

/// <summary>
/// The one hook registry, session-owned. Entries are (hook, phase, priority, stamp) keyed by
/// BattleEventTag type; each firing filters to its phase and sorts by priority (lower first)
/// then registration order. Matches are materialized before iteration, so a hook
/// unregistering itself mid-firing (one-shot mines) is safe and takes effect next dispatch.
/// </summary>
internal sealed class BattleHookRegistry
{
  private sealed record RegisteredHook(BattleHook Hook, HookPhase Phase, int Priority, long Stamp);

  private readonly BattleSession _session;
  private readonly EventKeyedRegistry<RegisteredHook> _hooks = new();
  private long _nextStamp;

  internal BattleHookRegistry(BattleSession session)
  {
    ArgumentNullException.ThrowIfNull(session);
    _session = session;
  }

  internal void Register<TEventKey>(BattleHook hook, HookPhase phase, int priority)
    where TEventKey : BattleEventTag
  {
    ArgumentNullException.ThrowIfNull(hook);
    RegisteredHook entry = new(hook, phase, priority, _nextStamp++);
    _hooks.Register<TEventKey>(entry);
  }

  internal bool Unregister<TEventKey>(BattleHook hook, HookPhase phase)
    where TEventKey : BattleEventTag
  {
    ArgumentNullException.ThrowIfNull(hook);
    return _hooks.RemoveFirst<TEventKey>(entry => ReferenceEquals(entry.Hook, hook) && entry.Phase == phase);
  }

  /// <summary>
  /// Fires the event's hooks for one phase, in priority-then-registration order, and returns
  /// the interrupt actions they produced (in evaluation order). The session routes non-empty
  /// results into the executor's action window — or throws when none is open.
  /// </summary>
  private IReadOnlyList<BattleAction> Fire(
    BattleEvent battleEvent,
    HookPhase phase,
    Option<BattleAction> sourceAction)
  {
    ArgumentNullException.ThrowIfNull(battleEvent);

    var matches = _hooks
      .GetMatching(battleEvent)
      .Where(entry => entry.Phase == phase)
      .OrderBy(entry => entry.Priority)
      .ThenBy(entry => entry.Stamp)
      .ToList();

    if (matches.Count == 0)
      return [];

    var context = new HookContext(_session, phase, sourceAction);
    List<BattleAction> interruptActions = [];
    foreach (RegisteredHook registered in matches)
      interruptActions.AddRange(registered.Hook.OnEvent(context, battleEvent));

    return interruptActions;
  }

  internal IReadOnlyList<BattleAction> RunPreHooks(BattleEvent battleEvent, Option<BattleAction> sourceAction) =>
    Fire(battleEvent, HookPhase.Before, sourceAction);
  internal IReadOnlyList<BattleAction> RunPostHooks(BattleEvent battleEvent, Option<BattleAction> sourceAction) =>
    Fire(battleEvent, HookPhase.After, sourceAction);
}

/// <summary>
/// Shared multimap keyed by <see cref="BattleEventTag"/> type. Items register against a tag
/// type (a concrete event record or a marker interface) and are looked up for a committed
/// event, so registrations against tag interfaces also fire.
/// </summary>
internal sealed class EventKeyedRegistry<T>
{
  private readonly Dictionary<Type, List<T>> _itemsByEventType = [];
  private readonly Dictionary<Type, Type[]> _keyCache = [];

  internal void Register<TEventKey>(T item)
    where TEventKey : BattleEventTag
  {
    ArgumentNullException.ThrowIfNull(item);
    Type eventKey = typeof(TEventKey);

    if (!_itemsByEventType.TryGetValue(eventKey, out var items))
      _itemsByEventType[eventKey] = items = [];

    items.Add(item);
  }

  /// <summary>Removes the first item under the key that satisfies the predicate; false if none does.</summary>
  internal bool RemoveFirst<TEventKey>(Func<T, bool> match)
    where TEventKey : BattleEventTag
  {
    if (!_itemsByEventType.TryGetValue(typeof(TEventKey), out var items))
      return false;

    int index = items.FindIndex(item => match(item));
    if (index < 0)
      return false;

    items.RemoveAt(index);
    return true;
  }

  /// <summary>
  /// Items matching the event, in key order — the event's concrete type first, then its tag
  /// interfaces in reflection order — and in registration order within each key.
  /// </summary>
  internal IEnumerable<T> GetMatching(BattleEvent battleEvent)
  {
    foreach (Type eventKey in KeysFor(battleEvent))
    {
      if (!_itemsByEventType.TryGetValue(eventKey, out var items))
        continue;

      foreach (T item in items)
        yield return item;
    }
  }
  
  private Type[] KeysFor(BattleEvent battleEvent)
  {
    Type eventType = battleEvent.GetType();
    if (_keyCache.TryGetValue(eventType, out Type[]? cached))
      return cached;

    Type[] keys =
    [
      eventType,
      .. eventType
        .GetInterfaces()
        .Where(interfaceType => typeof(BattleEventTag).IsAssignableFrom(interfaceType)),
    ];
    _keyCache[eventType] = keys;
    return keys;
  }
}
