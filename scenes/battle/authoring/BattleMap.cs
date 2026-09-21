using FunProject.Battle;
using Godot;

public partial class BattleMap : GridMap
{
  [Export] public required BattleMapData MapData { get; set; }
  [Export] public required BattleTilePalette UsedPalette { get; set; }
}