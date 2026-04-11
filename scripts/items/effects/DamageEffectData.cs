using FunProject.Weapons;
using Godot;

namespace FunProject.Items.Effects;

[GlobalClass]
public partial class DamageEffectData : BattleEffectData
{
  [Export] public int BaseDamage { get; set; }
  [Export] public DamageElement DamageElement { get; set; } = DamageElement.Kinetic;
  [Export] public bool DamageTerrain { get; set; }
}
