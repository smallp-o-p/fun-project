using FunProject.Core;
using Godot;

namespace FunProject.Items.Effects;

[GlobalClass]
public partial class DamageOverTimeStatusSpecData : StatusEffectSpecData
{
  [Export] public int TickDamage { get; set; } = 1;
  [Export] public Element TickElement { get; set; } = Element.Thermal;
}
