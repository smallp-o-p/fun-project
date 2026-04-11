#nullable enable
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
  UnitSelected,
  UnitMoved,
  UnitDamaged,
}

public readonly record struct BattleEvent(
  BattleEventType Type,
  int? UnitId = null,
  Vector3I? Position = null,
  string? Message = null
);
