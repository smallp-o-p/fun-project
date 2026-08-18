using Godot;

namespace FunProject.Battle;

[GlobalClass]
public partial class BattleMapData : Resource
{
  [Export] public Godot.Vector3I Dimensions { get; set; } = new(8, 1, 8);

  // Present cells keyed by position. A cell not in the dictionary is a hole (unwalkable). Each
  // value is a BattleMapTileData — the same type authored as an editor brush. Convert to a runtime
  // board with `new BattleBoardState(map)`.
  [Export] public Godot.Collections.Dictionary<Godot.Vector3I, BattleMapTileData> Tiles { get; set; } = [];
}
