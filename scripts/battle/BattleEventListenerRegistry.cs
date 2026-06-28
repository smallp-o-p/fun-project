using System;
using System.Collections.Generic;
using System.Linq;

namespace FunProject.Battle;

public enum ListenerPriority
{
  High,
  Regular,
  Low,
}

internal sealed class BattleEventListenerRegistry
{
  private sealed record RegisteredListener(
    BattleEventListener Listener,
    ListenerPriority Priority,
    long RegistrationOrder);

  private readonly EventKeyedRegistry<RegisteredListener> _registry = new();
  private long _nextRegistrationOrder;

  internal void Register<TEventKey>(BattleEventListener listener, ListenerPriority priority)
    where TEventKey : BattleEventTag
  {
    ArgumentNullException.ThrowIfNull(listener);
    _registry.Register<TEventKey>(new(listener, priority, _nextRegistrationOrder++));
  }

  /// <summary>
  /// Listeners fire in coarse priority bucket order (High -> Regular -> Low), with registration
  /// order winning within a bucket. The monotonic registration stamp makes the cross-key merge
  /// (an event matches its concrete type AND its tag interfaces) one global stable sort rather
  /// than a per-key one.
  /// </summary>
  internal IReadOnlyList<BattleEventListener> GetMatchingListeners(BattleEvent battleEvent)
  {
    if (_registry.Count == 0)
      return [];

    return [.. _registry
        .GetMatching(battleEvent)
        .OrderBy(entry => entry.Priority)
        .ThenBy(entry => entry.RegistrationOrder)
        .Select(entry => entry.Listener)];
  }
}
