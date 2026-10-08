using Godot;

namespace FunProject.Scenes;

// A finished mesh carries its resolved cells through Godot's native MeshLibrary export.
[Tool, GlobalClass]
public partial class BattleProp : MeshInstance3D
{
  public const string FootprintMeta = "battle_footprint";
  public Godot.Collections.Dictionary<Godot.Vector3I, BattleFootprintData> Footprint =>
    Mesh.GetMeta(FootprintMeta).AsGodotDictionary<Godot.Vector3I, BattleFootprintData>();
}
