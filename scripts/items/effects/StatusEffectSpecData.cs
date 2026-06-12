using Godot;

namespace FunProject.Items.Effects;

[GlobalClass]
public abstract partial class StatusEffectSpecData : BattleEffectData
{
  [Export] public int DurationTurns { get; set; } = 1;
  [Export] public int ApplyChancePercent { get; set; } = 100;
  [Export] public bool RequiresHealthDamage { get; set; }
}
