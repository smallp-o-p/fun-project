using FunProject.Battle;
using Godot;
using System;

public sealed partial class BattleEventSignalHandler : Node
{
  private Option<BattleRuntime> _runtime;

  public bool IsBound => _runtime.IsSome;

  [Signal]
  public delegate void PresentationEventCommittedEventHandler(BattleEventAdapter battleEvent);

  public void Bind(BattleRuntime runtime)
  {
    ArgumentNullException.ThrowIfNull(runtime);

    Unbind();
    _runtime = Some(runtime);
    runtime.BattleEventCommitted += ForwardBattleEvent;
  }

  public void Unbind()
  {
    _runtime.IfSome(runtime =>
    {
      runtime.BattleEventCommitted -= ForwardBattleEvent;
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
}
