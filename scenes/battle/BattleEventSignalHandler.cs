using FunProject.Battle;
using Godot;
using System;

public sealed partial class BattleEventSignalHandler : Node
{
  private Option<BattleRuntime> _runtime;

  public bool IsBound => _runtime.IsSome;

  [Signal]
  public delegate void PresentationEventCommittedEventHandler(BattleEventAdapter battleEvent);

  [Signal]
  public delegate void ActionStartedEventHandler(string actionId);

  [Signal]
  public delegate void ActionCompletedEventHandler(BattleActionResultAdapter result);

  public void Bind(BattleRuntime runtime)
  {
    ArgumentNullException.ThrowIfNull(runtime);

    Unbind();
    _runtime = Some(runtime);
    runtime.BattleEventCommitted += ForwardBattleEvent;
    runtime.ActionStarted += ForwardActionStarted;
    runtime.ActionCompleted += ForwardActionCompleted;
  }

  public void Unbind()
  {
    _runtime.IfSome(runtime =>
    {
      runtime.BattleEventCommitted -= ForwardBattleEvent;
      runtime.ActionStarted -= ForwardActionStarted;
      runtime.ActionCompleted -= ForwardActionCompleted;
      _runtime = None;
    });
  }

  public override void _ExitTree()
  {
    Unbind();
  }

  public override void _Notification(int what)
  {
    if (what == NotificationPredelete)
      Unbind();
  }

  private void ForwardBattleEvent(BattleEvent battleEvent)
  {
    EmitSignal(SignalName.PresentationEventCommitted, new BattleEventAdapter(battleEvent));
  }

  private void ForwardActionStarted(BattleAction action)
  {
    EmitSignal(SignalName.ActionStarted, action.ActionId);
  }

  private void ForwardActionCompleted(BattleActionResult result)
  {
    EmitSignal(SignalName.ActionCompleted, new BattleActionResultAdapter(result));
  }
}
