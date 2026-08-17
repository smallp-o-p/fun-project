using System;
using System.Collections.Generic;

namespace FunProject.Battle;

/// <summary>
/// The one hook registry, executor-owned. Entries are (hook, priority, stamp) keyed by
/// BattleEventTag type; each firing sorts by priority (lower first) then registration
/// order. Matches are materialized before iteration, and the registry retires hooks that
/// signal <see cref="BattleHook.NeedsToUnregister"/> right after their firing (one-shot
/// mines) — both safe mid-firing, both effective on the next dispatch.
/// </summary>
internal sealed class BattleHookRegistry
{
  private sealed record RegisteredHook(BattleHook Hook, int Priority, long Stamp);

  private readonly EventKeyedRegistry<RegisteredHook> _hooks = new();
  private long _nextStamp;

  internal void Register<TEventKey>(BattleHook hook, int priority)
    where TEventKey : BattleEventTag
  {
    ArgumentNullException.ThrowIfNull(hook);
    RegisteredHook entry = new(hook, priority, _nextStamp++);
    _hooks.Register<TEventKey>(entry);
  }

  internal bool Unregister<TEventKey>(BattleHook hook)
    where TEventKey : BattleEventTag
  {
    ArgumentNullException.ThrowIfNull(hook);
    return _hooks.RemoveFirst<TEventKey>(entry => ReferenceEquals(entry.Hook, hook));
  }

  /// <summary>
  /// Fires the event's hooks, in priority-then-registration order, retiring one-shot hooks
  /// that signal <see cref="BattleHook.NeedsToUnregister"/> as soon as their firing
  /// returns, and returns the interrupt actions they produced (in evaluation order).
  /// Interrupts are routed into the executor's action window — or throw when none is open.
  /// </summary>
  public IReadOnlyList<BattleAction> Fire(BattleEvent battleEvent, HookContext context)
  {
    ArgumentNullException.ThrowIfNull(battleEvent);

    RegisteredHook[] matches =
    [
      .. _hooks
        .GetMatching(battleEvent)
        .AsValueEnumerable().OrderBy(entry => entry.Priority)
        .ThenBy(entry => entry.Stamp)
        .DistinctBy(entry => entry.Hook, System.Collections.Generic.ReferenceEqualityComparer.Instance),
    ];

    List<BattleAction> interrupts = [];
    foreach (RegisteredHook registered in matches)
    {
      interrupts.AddRange(registered.Hook.OnEvent(context, battleEvent));
      if (registered.Hook.NeedsToUnregister)
        _hooks.RemoveAll(entry => ReferenceEquals(entry.Hook, registered.Hook));
    }

    return interrupts;
  }
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

  /// <summary>Removes every item satisfying the predicate across all keys — a spent one-shot hook retired by the registry.</summary>
  internal void RemoveAll(Func<T, bool> match)
  {
    foreach (var items in _itemsByEventType.Values)
      items.RemoveAll(item => match(item));
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
        .AsValueEnumerable().Where(interfaceType => typeof(BattleEventTag).IsAssignableFrom(interfaceType)),
    ];
    _keyCache[eventType] = keys;
    return keys;
  }
}
