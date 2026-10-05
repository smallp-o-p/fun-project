using FunProject.Battle;
using Godot;

/// <summary>
/// DO NOT MODIFY!!! This an exported map that can be used in a tactical skirmish.
/// </summary>
public partial class BattleMap : Node3D
{
  [Export] public required BattleMapData MapData { get; set; }
  [Export] public GridMap FloorTiles { get; set; } = null!;
  [Export] public GridMap PropTiles { get; set; } = null!;
}
