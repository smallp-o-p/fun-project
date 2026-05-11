using System;
using System.Collections.Generic;
using System.Linq;

namespace FunProject.Battle;

internal sealed class BattleTriggerRegistry
{
  private sealed record RegisteredTrigger(
    BattleTrigger Trigger,
    long RegistrationOrder,
    IReadOnlyList<BattleEventType> EventTypes);

  private readonly Dictionary<BattleEventType, List<RegisteredTrigger>> _registeredTriggersByEventType = [];
  private long _nextRegistrationOrder;

  internal void Register(BattleTrigger trigger, BattleEventType eventType)
  {
    Register(trigger, [eventType]);
  }

  internal void Register(BattleTrigger trigger, IEnumerable<BattleEventType> eventTypes)
  {
    ArgumentNullException.ThrowIfNull(trigger);
    ArgumentNullException.ThrowIfNull(eventTypes);
    ArgumentException.ThrowIfNullOrWhiteSpace(trigger.TriggerId);

    IReadOnlyList<BattleEventType> distinctEventTypes = GetDistinctEventTypes(eventTypes);
    RegisteredTrigger registeredTrigger = new(trigger, _nextRegistrationOrder++, distinctEventTypes);

    foreach (BattleEventType eventType in distinctEventTypes)
    {
      if (!_registeredTriggersByEventType.TryGetValue(eventType, out List<RegisteredTrigger> registeredTriggers))
      {
        registeredTriggers = [];
        _registeredTriggersByEventType.Add(eventType, registeredTriggers);
      }

      registeredTriggers.Add(registeredTrigger);
    }
  }

  private bool Unregister(RegisteredTrigger registeredTrigger)
  {
    ArgumentNullException.ThrowIfNull(registeredTrigger);

    foreach (BattleEventType eventType in registeredTrigger.EventTypes)
    {
      if (!_registeredTriggersByEventType.TryGetValue(eventType, out List<RegisteredTrigger> registeredTriggers))
        continue;

      registeredTriggers.Remove(registeredTrigger);
      if (registeredTriggers.Count == 0)
        _registeredTriggersByEventType.Remove(eventType);
    }

    return true;
  }

  internal IReadOnlyList<BattleAction> EvaluateInterruptActions(
    BattleSession session,
    BattleEvent battleEvent,
    BattleAction sourceAction)
  {
    ArgumentNullException.ThrowIfNull(session);
    ArgumentNullException.ThrowIfNull(battleEvent);
    ArgumentNullException.ThrowIfNull(sourceAction);

    if (!_registeredTriggersByEventType.TryGetValue(battleEvent.Type, out List<RegisteredTrigger> eventTypeTriggers))
      return [];

    List<RegisteredTrigger> matchingTriggers =
    [
      .. eventTypeTriggers
        .Where(entry => entry.Trigger.Matches(battleEvent))
        .OrderBy(entry => entry.Trigger.Priority)
        .ThenBy(entry => entry.RegistrationOrder),
    ];

    List<BattleAction> interruptActions = [];
    foreach (var registeredTrigger in matchingTriggers)
    {
      BattleTriggerResult result = registeredTrigger.Trigger.Evaluate(session, battleEvent, sourceAction);
      ArgumentNullException.ThrowIfNull(result.InterruptActions);
      interruptActions.AddRange(result.InterruptActions);

      if (result.ShouldConsumeTrigger)
        Unregister(registeredTrigger);
    }

    return interruptActions;
  }

  private static IReadOnlyList<BattleEventType> GetDistinctEventTypes(IEnumerable<BattleEventType> eventTypes)
  {
    List<BattleEventType> distinctEventTypes = [];
    SysColGeneric.HashSet<BattleEventType> seenEventTypes = [];
    foreach (BattleEventType eventType in eventTypes)
    {
      if (seenEventTypes.Add(eventType))
        distinctEventTypes.Add(eventType);
    }

    if (distinctEventTypes.Count == 0)
      throw new ArgumentException("At least one event type must be provided.", nameof(eventTypes));

    return distinctEventTypes;
  }
}
