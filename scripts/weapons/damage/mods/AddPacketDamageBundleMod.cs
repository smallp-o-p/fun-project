using System;
using System.Collections.Generic;
using Godot;

namespace FunProject.Weapons;

[GlobalClass]
public partial class AddPacketDamageBundleMod : DamageBundleMod
{
  [Export] public DamagePacketData Packet { get; set; }

  public override List<Damage> Apply(List<Damage> bundle, DamageEmissionContext context)
  {
    ArgumentNullException.ThrowIfNull(Packet);
    return [.. bundle, Packet.Derive(context.BaseDamage)];
  }
}
