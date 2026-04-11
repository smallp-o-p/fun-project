using Godot;

namespace FunProject.Items.Effects;

[GlobalClass]
public partial class SpawnHazardEffectData : BattleEffectData
{
  [Export] public string HazardId { get; set; } = "";
  [Export] public int DurationTurns { get; set; } = 1;
}
