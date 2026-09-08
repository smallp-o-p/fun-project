using System;
using System.Collections.Generic;
using FunProject.Core;
using FunProject.Weapons;

namespace FunProject.Battle;

public readonly record struct ArmorState(int Current, Element Element);

public readonly record struct DamageResolution(int ArmorDamage, int HealthDamage, int StunDamage = 0);

/// <summary>
/// Pure damage split. Packets resolve in bundle order; health packets see the armor
/// remaining after earlier packets. A packet whose element matches the armor strips 1.5x
/// (floored) from armor, but health spill is always computed from the un-multiplied amount —
/// an element match never increases health damage. Stun packets bypass armor.
/// </summary>
public static class DamageResolver
{
  public static DamageResolution Resolve(IReadOnlyList<Damage> bundle, Option<ArmorState> armor)
    => Resolve(ResolvePackets(bundle, armor));

  public static DamageResolution Resolve(IReadOnlyList<DamageResolution> packets)
  {
    ArgumentNullException.ThrowIfNull(packets);

    int armorDamage = 0;
    int healthDamage = 0;
    int stunDamage = 0;
    foreach (DamageResolution packet in packets)
    {
      armorDamage += packet.ArmorDamage;
      healthDamage += packet.HealthDamage;
      stunDamage += packet.StunDamage;
    }

    return new DamageResolution(armorDamage, healthDamage, stunDamage);
  }

  /// <summary>
  /// Per-packet splits, index-aligned with the input bundle. Packets with a
  /// non-positive amount resolve to (0, 0, 0) so alignment holds.
  /// </summary>
  public static IReadOnlyList<DamageResolution> ResolvePackets(IReadOnlyList<Damage> bundle, Option<ArmorState> armor)
  {
    ArgumentNullException.ThrowIfNull(bundle);

    (int armorRemaining, Element armorElement) = armor.Match(
      state =>
      {
        ArgumentOutOfRangeException.ThrowIfNegative(state.Current);
        return (state.Current, state.Element);
      },
      () => (0, default(Element)));

    List<DamageResolution> resolutions = new(bundle.Count);
    foreach (Damage packet in bundle)
    {
      if (packet.Amount <= 0)
      {
        resolutions.Add(new DamageResolution(0, 0, 0));
        continue;
      }

      if (packet.Kind == DamageKind.Stun)
      {
        resolutions.Add(new DamageResolution(0, 0, packet.Amount));
        continue;
      }
      if (packet.Kind != DamageKind.Health)
        throw new ArgumentOutOfRangeException(nameof(bundle), packet.Kind, "Unknown damage kind.");

      int healthDamage = Math.Max(packet.Amount - armorRemaining, 0);
      int armorLoss = 0;
      if (armorRemaining > 0)
      {
        bool matched = packet.Element == armorElement;
        armorLoss = Math.Min(armorRemaining, matched ? packet.Amount + packet.Amount / 2 : packet.Amount);
        armorRemaining -= armorLoss;
      }

      resolutions.Add(new DamageResolution(armorLoss, healthDamage));
    }

    return resolutions;
  }
}
