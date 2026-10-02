using Godot;

[Tool, GlobalClass]
public partial class BattleFloor : Node3D
{
  [Export] public BattleFootprintData Footprint { get; set; } = new();
}
