using Godot;

[Tool, GlobalClass]
public partial class BattleFootprintData : Resource
{
  [Export] public Godot.Collections.Dictionary<Godot.Vector3I, BattleFootprintCellData> Cells { get; set; } = [];
}
