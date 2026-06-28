using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace FunProject.Weapons;

[GlobalClass]
public partial class DamageBundleMod : Resource
{
  [Export] public Godot.Collections.Array<PacketModifier> PacketModifiers { get; set; } = [];
  [Export] public Godot.Collections.Array<DamagePacketData> AddedPackets { get; set; } = [];

  public List<Damage> Apply(List<Damage> bundle, DamageEmissionContext context)
  {
    IEnumerable<Damage> result = bundle;
    foreach (PacketModifier modifier in PacketModifiers)
    {
      ArgumentNullException.ThrowIfNull(modifier);
      result = modifier.Apply(result);
    }

    List<Damage> emitted = result.ToList();
    foreach (DamagePacketData packet in AddedPackets)
    {
      ArgumentNullException.ThrowIfNull(packet);
      emitted.Add(packet.Derive(context.BaseDamage));
    }

    return emitted;
  }
}
