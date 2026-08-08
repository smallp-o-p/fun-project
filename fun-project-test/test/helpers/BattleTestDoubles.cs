using FunProject.Battle;
using Godot;
using System.Collections.Generic;

namespace FunProject.Tests;

// Collects every event it receives; register with executor/runtime RegisterHook<TEventKey>.
internal sealed class RecordingHook : BattleHook
{
  public List<BattleEvent> Received { get; } = [];

  public override IReadOnlyList<BattleAction> OnEvent(HookContext context, BattleEvent battleEvent)
  {
    Received.Add(battleEvent);
    return [];
  }
}

// Logs a message when it sees a positioned event on the target tile; counts every evaluation.
internal sealed partial class PositionRecordingHook : BattleHook
{
  private readonly Vector3I _position;
  private readonly List<string> _log;
  private readonly string _message;

  public int EvaluateCallCount { get; private set; }

  public PositionRecordingHook(Vector3I position, List<string> log, string message)
  {
    _position = position;
    _log = log;
    _message = message;
  }

  public override IReadOnlyList<BattleAction> OnEvent(HookContext context, BattleEvent battleEvent)
  {
    EvaluateCallCount++;
    if (battleEvent is not IPositionedBattleEvent positioned || positioned.Position.Raw != _position)
      return [];

    _log.Add(_message);
    return [];
  }
}
