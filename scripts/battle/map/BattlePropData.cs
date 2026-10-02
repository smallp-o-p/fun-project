using Godot;

namespace FunProject.Battle;

// Static scenery metadata. The scene authoring bake folds these flags into fresh ground cells.
[Tool, GlobalClass]
public partial class BattlePropData : Resource
{
  [Export] public Godot.Collections.Array<Godot.Vector3I> Footprint { get; set; } = [];
  [Export] public bool BlocksMovement { get; set; }
  [Export] public bool BlocksLineOfSight { get; set; }
  [Export] public Godot.Collections.Array<BattlePropCoverEdgeData> CoverEdges { get; set; } = [];
}
