using FunProject.Battle;
using Godot;

public partial class BattleMap : Node3D
{
  [Export] public required BattleMapData MapData { get; set; }
}
