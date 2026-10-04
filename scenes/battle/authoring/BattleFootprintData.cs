using FunProject.Battle;
using Godot;

// Authored contributions for one local grid cell; the baker derives final standability.
[Tool, GlobalClass]
public partial class BattleFootprintData : Resource
{
  [Export] public bool HasFloor { get; set; }
  [Export] public bool BlocksMovement { get; set; }
  [Export] public bool BlocksLineOfSight { get; set; }
  [Export] public bool BlocksVerticalLineOfSight { get; set; }
  [Export] public bool WalkableTop { get; set; }
  [Export] public bool TopBlocksVerticalLineOfSight { get; set; }
  [Export] public CoverDirections CoverDirections { get; set; }
  [Export(PropertyHint.Range, "0,100")] public int CoverAmount { get; set; }
}
