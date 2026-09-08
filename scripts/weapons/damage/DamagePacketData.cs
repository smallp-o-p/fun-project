using FunProject.Core;
using FunProject.Items.Effects;
using Godot;

namespace FunProject.Weapons;

[GlobalClass]
public partial class DamagePacketData : Resource
{
  [Export] public Element Element { get; set; } = Element.Kinetic;
  [Export] public float Multiplier { get; set; } = 1.0f;
  [Export] public StatusEffectSpecData? Status { get; set; }
  [Export] public DamageKind Kind { get; set; } = DamageKind.Health;

  public Damage Derive(int baseDamage)
    => new(Mathf.RoundToInt(baseDamage * Multiplier), Element, Optional(Status), Kind);
}
