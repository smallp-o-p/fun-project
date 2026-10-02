using Godot;

namespace FunProject.Battle;

// One outward-facing footprint boundary. Direction is exactly one cardinal heading.
[Tool, GlobalClass]
public partial class BattlePropCoverEdgeData : Resource
{
  [Export] public Godot.Vector3I Cell { get; set; }
  [Export] public CoverDirections Direction { get; set; }
  [Export(PropertyHint.Range, "0,100,1")] public int Amount { get; set; }
}
