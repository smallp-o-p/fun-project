using System;
using System.Collections.Generic;

namespace FunProject.Battle;

/// <summary>
/// Shared multimap keyed by <see cref="BattleEventTag"/> type. Items register against a tag
/// type (a concrete event record or a marker interface) and are looked up for a committed
/// event via <see cref="BattleEventKeys.For"/>, so registrations against tag interfaces also
/// fire. Backs both <see cref="BattleTriggerRegistry"/> and <see cref="BattleEventListenerRegistry"/>;
/// each wrapper keeps its own logic (sort/consume, fast paths) on top.
/// </summary>
internal sealed class EventKeyedRegistry<T>
{
  private readonly Dictionary<Type, List<T>> _itemsByEventType = [];

  internal int Count => _itemsByEventType.Count;

  internal void Register<TEventKey>(T item)
    where TEventKey : BattleEventTag
  {
    ArgumentNullException.ThrowIfNull(item);
    Type eventKey = typeof(TEventKey);

    if (!_itemsByEventType.TryGetValue(eventKey, out var items))
      _itemsByEventType[eventKey] = items = [];

    items.Add(item);
  }

  internal void Remove(Type eventKey, T item)
  {
    _itemsByEventType[eventKey].Remove(item);
  }

  /// <summary>
  /// Items matching the event, in key order — the event's concrete type first, then its tag
  /// interfaces in reflection order — and in registration order within each key.
  /// </summary>
  internal IEnumerable<T> GetMatching(BattleEvent battleEvent)
  {
    foreach (Type eventKey in BattleEventKeys.For(battleEvent))
    {
      if (!_itemsByEventType.TryGetValue(eventKey, out var items))
        continue;

      foreach (T item in items)
        yield return item;
    }
  }
}
