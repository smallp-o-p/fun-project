using FunProject.Battle;
using Godot;
using System.Collections.Generic;

namespace FunProject.Tests;

// Collects every event it receives; register against the session with RegisterListener<TEventKey>.
internal sealed class RecordingBattleEventListener : BattleEventListener
{
  public List<BattleEvent> Received { get; } = [];

  public override void OnEventCommitted(BattleSession session, BattleEvent battleEvent)
    => Received.Add(battleEvent);
}

// A trigger that logs a message the first time it sees a positioned event on the target tile.
// Optionally counts every evaluation and/or consumes itself when it fires.
internal sealed partial class RecordingTrigger : BattleTrigger
{
  private readonly Vector3I _position;
  private readonly List<string> _log;
  private readonly string _message;
  private readonly bool _consume;

  public int EvaluateCallCount { get; private set; }

  public RecordingTrigger(Vector3I position, List<string> log, string message, int priority = 0, bool consume = false)
  {
    _position = position;
    _log = log;
    _message = message;
    _consume = consume;
    Priority = priority;
  }

  public override BattleTriggerResult Evaluate(BattleSession session, BattleEvent battleEvent, BattleAction sourceAction)
  {
    EvaluateCallCount++;
    if (battleEvent is not IPositionedBattleEvent positioned || positioned.Position.Raw != _position)
      return BattleTriggerResult.NoReaction();

    _log.Add(_message);
    return _consume ? BattleTriggerResult.ConsumeTrigger() : BattleTriggerResult.NoReaction();
  }
}
