using Godot;

namespace FunProject.Battle;

[GlobalClass]
public partial class BattleMapData : Resource
{
  [Export] public Godot.Vector3I Dimensions { get; set; }
  [Export] public Godot.Collections.Dictionary<Godot.Vector3I, BattleMapTileData> Tiles { get; set; } = [];
}
