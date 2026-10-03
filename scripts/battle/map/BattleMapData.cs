using Godot;

namespace FunProject.Battle;

[Tool, GlobalClass]
public partial class BattleMapData : Resource
{
  [Export] public Vector3 GridOrigin { get; set; }
  [Export] public float CellWidth { get; set; } = 1;
  [Export] public float LevelHeight { get; set; } = 1;
  [Export] public Godot.Vector3I Dimensions { get; set; }
  [Export] public Godot.Collections.Dictionary<Godot.Vector3I, BattleMapTileData> Tiles { get; set; } = [];
}
