using FunProject.Core;
using Godot;

namespace FunProject.Items.Effects;

[GlobalClass]
public partial class DamageEffectData : BattleEffectData
{
  [Export] public int BaseDamage { get; set; }
  [Export] public Element Element { get; set; } = Element.Kinetic;
  [Export] public bool DamageTerrain { get; set; }
}
