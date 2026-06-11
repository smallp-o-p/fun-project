using System;
using System.Collections.Generic;
using System.Linq;

namespace FunProject.Battle;

internal sealed class BattleTriggerRegistry
{
  private sealed record RegisteredTrigger(
    BattleTrigger Trigger,
    long RegistrationOrder,
    Type EventType);

  private readonly Dictionary<Type, List<RegisteredTrigger>> _registeredTriggersByEventType = [];
  private long _nextRegistrationOrder;

  internal void Register<TEventKey>(BattleTrigger trigger)
    where TEventKey : BattleEventTag
  {
    ArgumentNullException.ThrowIfNull(trigger);
    Type eventKey = typeof(TEventKey);

    if (!_registeredTriggersByEventType.TryGetValue(eventKey, out var registeredTriggers))
      _registeredTriggersByEventType[eventKey] = registeredTriggers = [];

    registeredTriggers.Add(new(trigger, _nextRegistrationOrder++, eventKey));
  }

  private void Unregister(RegisteredTrigger registeredTrigger)
  {
    _registeredTriggersByEventType[registeredTrigger.EventType].Remove(registeredTrigger);
  }

  internal IReadOnlyList<BattleAction> EvaluateInterruptActions(
    BattleSession session,
    BattleEvent battleEvent,
    BattleAction sourceAction)
  {
    ArgumentNullException.ThrowIfNull(session);
    ArgumentNullException.ThrowIfNull(battleEvent);
    ArgumentNullException.ThrowIfNull(sourceAction);

    IReadOnlyList<RegisteredTrigger> matchingTriggers = GetMatchingTriggers(battleEvent);
    if (matchingTriggers.Count == 0)
      return [];

    List<BattleAction> interruptActions = [];
    foreach (var registeredTrigger in matchingTriggers)
    {
      BattleTriggerResult result = registeredTrigger.Trigger.Evaluate(session, battleEvent, sourceAction);
      interruptActions.AddRange(result.InterruptActions);

      if (result.Consumed)
        Unregister(registeredTrigger);
    }

    return interruptActions;
  }

  private IReadOnlyList<RegisteredTrigger> GetMatchingTriggers(BattleEvent battleEvent)
  {
    List<RegisteredTrigger> matchingTriggers = [];
    foreach (Type eventKey in BattleEventKeys.For(battleEvent))
    {
      if (!_registeredTriggersByEventType.TryGetValue(eventKey, out var registeredTriggers))
        continue;

      foreach (RegisteredTrigger registeredTrigger in registeredTriggers)
      {
        if (registeredTrigger.Trigger.Matches(battleEvent))
          matchingTriggers.Add(registeredTrigger);
      }
    }

    return [.. matchingTriggers
        .OrderBy(entry => entry.Trigger.Priority)
        .ThenBy(entry => entry.RegistrationOrder)];
  }
}
