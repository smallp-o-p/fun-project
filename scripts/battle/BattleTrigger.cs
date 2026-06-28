using System;
using System.Collections.Generic;

namespace FunProject.Battle;

public abstract class BattleTrigger
{
  public int Priority { get; set; }

  public abstract BattleTriggerResult Evaluate(BattleSession session, BattleEvent battleEvent, BattleAction sourceAction);
}

public readonly record struct BattleTriggerResult(
  IReadOnlyList<BattleAction> InterruptActions,
  bool Consumed
)
{
  public IReadOnlyList<BattleAction> InterruptActions { get; init; } = InterruptActions ?? [];

  public static BattleTriggerResult NoReaction()
  {
    return new BattleTriggerResult([], false);
  }

  public static BattleTriggerResult QueueInterruptAfterCommit(BattleAction action, bool shouldConsumeTrigger = false)
  {
    ArgumentNullException.ThrowIfNull(action);
    return new BattleTriggerResult([action], shouldConsumeTrigger);
  }

  public static BattleTriggerResult ConsumeTrigger()
  {
    return new BattleTriggerResult([], true);
  }
}
