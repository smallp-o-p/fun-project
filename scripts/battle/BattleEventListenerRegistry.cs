using System;
using System.Collections.Generic;

namespace FunProject.Battle;

internal sealed class BattleEventListenerRegistry
{
  private readonly Dictionary<Type, List<BattleEventListener>> _listenersByEventType = [];

  internal void Register<TEventKey>(BattleEventListener listener)
    where TEventKey : BattleEventTag
  {
    ArgumentNullException.ThrowIfNull(listener);
    Type eventKey = typeof(TEventKey);

    if (!_listenersByEventType.TryGetValue(eventKey, out var listeners))
      _listenersByEventType[eventKey] = listeners = [];

    listeners.Add(listener);
  }

  /// <summary>
  /// Listeners fire in key order — the event's concrete type first, then its tag
  /// interfaces in reflection order — and in registration order within each key.
  /// </summary>
  internal IReadOnlyList<BattleEventListener> GetMatchingListeners(BattleEvent battleEvent)
  {
    if (_listenersByEventType.Count == 0)
      return [];
    List<BattleEventListener> matching = [];
    foreach (Type eventKey in BattleEventKeys.For(battleEvent))
    {
      if (_listenersByEventType.TryGetValue(eventKey, out var listeners))
        matching.AddRange(listeners);
    }

    return matching;
  }
}
