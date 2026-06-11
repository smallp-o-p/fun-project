using FunProject.Core;
using Godot;

namespace FunProject.Weapons;

[GlobalClass]
public partial class DamagePacketData : Resource
{
  [Export] public Element Element { get; set; } = Element.Kinetic;
  [Export] public float Multiplier { get; set; } = 1.0f;

  public Damage Derive(int baseDamage)
    => new(Mathf.RoundToInt(baseDamage * Multiplier), Element);
}
