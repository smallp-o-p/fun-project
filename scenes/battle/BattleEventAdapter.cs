using FunProject.Battle;
using Godot;
using System;

public sealed partial class BattleEventAdapter : RefCounted
{
  public BattleEvent BattleEvent { get; init; }
  public string EventName => BattleEvent.EventName;
  public string Message => BattleEvent.ToDisplayString();

  public BattleEventAdapter(BattleEvent battleEvent)
  {
    ArgumentNullException.ThrowIfNull(battleEvent);
    BattleEvent = battleEvent;
  }
}
