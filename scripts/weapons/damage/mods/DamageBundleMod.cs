using System;
using System.Collections.Generic;
using Godot;

namespace FunProject.Weapons;

[GlobalClass]
public partial class DamageBundleMod : Resource
{
  [Export] public Godot.Collections.Array<PacketModifier> PacketModifiers { get; set; } = [];
  [Export] public Godot.Collections.Array<DamagePacketData> AddedPackets { get; set; } = [];

  [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "MA0015", Justification = "The guards identify invalid authored PacketModifiers and AddedPackets, not the supplied damage bundle.")]
  public List<Damage> Apply(List<Damage> bundle, DamageEmissionContext context)
  {
    IEnumerable<Damage> result = bundle;
    foreach (PacketModifier modifier in PacketModifiers)
    {
      ArgumentNullException.ThrowIfNull(modifier);
      result = modifier.Apply(result);
    }

    List<Damage> emitted = result.AsValueEnumerable().ToList();
    foreach (DamagePacketData packet in AddedPackets)
    {
      ArgumentNullException.ThrowIfNull(packet);
      emitted.Add(packet.Derive(context.BaseDamage));
    }

    return emitted;
  }
}
