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

  private readonly EventKeyedRegistry<RegisteredTrigger> _registry = new();
  private long _nextRegistrationOrder;

  internal void Register<TEventKey>(BattleTrigger trigger)
    where TEventKey : BattleEventTag
  {
    ArgumentNullException.ThrowIfNull(trigger);
    _registry.Register<TEventKey>(new(trigger, _nextRegistrationOrder++, typeof(TEventKey)));
  }

  private void Unregister(RegisteredTrigger registeredTrigger)
  {
    _registry.Remove(registeredTrigger.EventType, registeredTrigger);
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
    return [.. _registry
        .GetMatching(battleEvent)
        .OrderBy(entry => entry.Trigger.Priority)
        .ThenBy(entry => entry.RegistrationOrder)];
  }
}
