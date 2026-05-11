using Godot;
using System;
using System.Collections.Generic;

namespace FunProject.Battle;

public abstract partial class BattleTrigger : Resource
{
  public string TriggerId { get; set; } = string.Empty;
  public int Priority { get; set; }

  protected BattleTrigger()
  {
  }

  protected BattleTrigger(string triggerId, int priority = 0)
  {
    if (string.IsNullOrWhiteSpace(triggerId))
      throw new ArgumentException("Trigger id cannot be null or whitespace.", nameof(triggerId));

    TriggerId = triggerId;
    Priority = priority;
  }

  public abstract bool Matches(BattleEvent battleEvent);
  public abstract BattleTriggerResult Evaluate(BattleSession session, BattleEvent battleEvent, BattleAction sourceAction);
}

public readonly record struct BattleTriggerResult(
  IReadOnlyList<BattleAction> InterruptActions,
  bool ShouldConsumeTrigger
)
{
  public static BattleTriggerResult NoReaction()
  {
    return new BattleTriggerResult([], false);
  }

  public static BattleTriggerResult QueueInterruptAfterCommit(BattleAction action, bool shouldConsumeTrigger = false)
  {
    ArgumentNullException.ThrowIfNull(action);
    return new BattleTriggerResult([action], shouldConsumeTrigger);
  }

  public static BattleTriggerResult QueueInterruptAfterCommit(IReadOnlyList<BattleAction> actions, bool shouldConsumeTrigger = false)
  {
    ArgumentNullException.ThrowIfNull(actions);
    return new BattleTriggerResult(actions, shouldConsumeTrigger);
  }

  public static BattleTriggerResult ConsumeTrigger()
  {
    return new BattleTriggerResult([], true);
  }
}
