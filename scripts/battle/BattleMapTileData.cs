using Godot;

namespace FunProject.Battle;

[GlobalClass]
public partial class BattleMapTileData : Resource
{
  [Export] public Vector3I Coordinates { get; set; } = Vector3I.Zero;
  [Export] public bool IsPresent { get; set; } = true;
  [Export] public bool IsWalkable { get; set; } = true;
  [Export] public bool BlocksLineOfSight { get; set; }
}
