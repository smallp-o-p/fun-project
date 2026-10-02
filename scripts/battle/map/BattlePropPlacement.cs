namespace FunProject.Battle;

public readonly record struct BattlePropPlacement(
  BattlePropData Definition, Godot.Vector3I Anchor, int QuarterTurns);
