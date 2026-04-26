using Godot;

namespace FunProject.Battle;

public enum BattleEventType
{
  SessionStarted,
  SessionEnded,
  TurnStarted,
  TurnEnded,
  ActiveSideChanged,
  UnitAdded,
  UnitActivationEnded,
  UnitMoved,
  UnitDamaged,
  UnitKilled,
  ItemThrown,
}

public readonly record struct BattleEvent(
  BattleEventType Type,
  Option<int> UnitId = default,
  Option<Vector3I> Position = default,
  Option<string> Message = default
);
