using Godot;

namespace FunProject.Items.Effects;

[GlobalClass]
public partial class StatusEffectData : BattleEffectData
{
  [Export] public string StatusId { get; set; } = "";
  [Export] public int DurationTurns { get; set; } = 1;
}
