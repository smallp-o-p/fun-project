using FunProject.Battle;
using Godot;
using System;

public sealed partial class BattleEventAdapter : RefCounted
{
  public BattleEvent BattleEvent { get; init; }
  public string EventName => BattleEvent.GetType().Name;

  public BattleEventAdapter(BattleEvent battleEvent)
  {
    ArgumentNullException.ThrowIfNull(battleEvent);
    BattleEvent = battleEvent;
  }
}
