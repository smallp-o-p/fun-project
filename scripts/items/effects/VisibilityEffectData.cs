using Godot;

namespace FunProject.Items.Effects;

[GlobalClass]
public partial class VisibilityEffectData : BattleEffectData
{
  [Export] public int VisionDelta { get; set; }
  [Export] public bool CreatesSmokeCloud { get; set; }
}
