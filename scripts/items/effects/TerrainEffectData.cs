using Godot;

namespace FunProject.Items.Effects;

[GlobalClass]
public partial class TerrainEffectData : BattleEffectData
{
  [Export] public bool DestroysCover { get; set; }
  [Export] public bool IgnitesTiles { get; set; }
}
