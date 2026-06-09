using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace FunProject.Battle;

internal sealed class BattleTriggerRegistry
{
  private sealed record RegisteredTrigger(
    BattleTrigger Trigger,
    long RegistrationOrder,
    Type EventType);

  private readonly Dictionary<Type, List<RegisteredTrigger>> _registeredTriggersByEventType = [];
  private long _nextRegistrationOrder;

  public BattleTriggerRegistry()
  {
    Assembly assembly = typeof(BattleEventTag).Assembly;
    IEnumerable<Type> arr = assembly.GetTypes().Where(t => typeof(BattleEventTag).IsAssignableFrom(t));

    foreach (Type eventType in arr)
    {
      _registeredTriggersByEventType.Add(eventType, []);
    }
  }

  internal void Register<TEventKey>(BattleTrigger trigger)
    where TEventKey : BattleEventTag
  {
    ArgumentNullException.ThrowIfNull(trigger);
    Type eventKey = typeof(TEventKey);

    if (!_registeredTriggersByEventType.TryGetValue(eventKey, out var registeredTriggers))
      throw new InvalidOperationException("Register: BattleTriggerRegistry doesn't have a subscription for this particular BattleEvent");

    registeredTriggers.Add(new(trigger, _nextRegistrationOrder++, eventKey));
  }

  private void Unregister(RegisteredTrigger registeredTrigger)
  {
    ArgumentNullException.ThrowIfNull(registeredTrigger);

    if (!_registeredTriggersByEventType.TryGetValue(registeredTrigger.EventType, out var registeredTriggers))
      throw new InvalidOperationException("Unregister: BattleTriggerRegistry doesn't have a subscription for this particular BattleEvent");

    registeredTriggers.Remove(registeredTrigger);
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
      interruptActions.AddRange(result.InterruptActions ?? []);

      if (result.Consumed)
        Unregister(registeredTrigger);
    }

    return interruptActions;
  }

  private IReadOnlyList<RegisteredTrigger> GetMatchingTriggers(BattleEvent battleEvent)
  {
    List<RegisteredTrigger> matchingTriggers = [];
    foreach (Type eventKey in GetEventKeys(battleEvent))
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

  /*
  * This function gets all the keys associated with a particular BattleEvent.
  * E.g. For UnitMovedBattleEvent which implements BattleEvent, IUnitBattleEvent, IPositionedBattleEvent, ISourcePositionedBattleEvent
  * This will return a list of [UnitMovedBattleEvent, BattleEvent, IUnitBattleEvent, IPositionedBattleEvent], so that any registered triggers for the
  * abstract base classes get tripped.
  */
  private static IReadOnlyList<Type> GetEventKeys(BattleEvent battleEvent)
  {

    Type eventType = battleEvent.GetType();
    return
    [
      eventType,
      .. eventType
        .GetInterfaces()
        .Where(interfaceType => typeof(BattleEventTag).IsAssignableFrom(interfaceType)),
    ];
  }
}
