using Godot;

[Tool, GlobalClass]
public partial class BattleProp : Node3D
{
  [Export] public BattleFootprintData Footprint { get; set; } = new();
}
