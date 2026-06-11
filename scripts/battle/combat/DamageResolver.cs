using System;
using System.Collections.Generic;
using FunProject.Core;
using FunProject.Weapons;

namespace FunProject.Battle;

public readonly record struct ArmorState(int Current, Element Element);

public readonly record struct DamageResolution(int ArmorDamage, int HealthDamage);

/// <summary>
/// Pure armor-vs-health damage split. Packets resolve in bundle order; each packet
/// sees the armor remaining after earlier packets. A packet whose element matches the
/// armor strips 1.5x (floored) from armor, but health spill is always computed from
/// the un-multiplied amount — an element match never increases health damage.
/// </summary>
public static class DamageResolver
{
  public static DamageResolution Resolve(IReadOnlyList<Damage> bundle, Option<ArmorState> armor)
  {
    ArgumentNullException.ThrowIfNull(bundle);

    (int armorRemaining, Element armorElement) = armor.Match(
      state =>
      {
        ArgumentOutOfRangeException.ThrowIfNegative(state.Current);
        return (state.Current, state.Element);
      },
      () => (0, default(Element)));

    int armorDamage = 0;
    int healthDamage = 0;
    foreach (Damage packet in bundle)
    {
      if (packet.Amount <= 0)
        continue;

      healthDamage += Math.Max(packet.Amount - armorRemaining, 0);
      if (armorRemaining > 0)
      {
        bool matched = packet.Element == armorElement;
        int armorLoss = Math.Min(armorRemaining, matched ? packet.Amount + packet.Amount / 2 : packet.Amount);
        armorDamage += armorLoss;
        armorRemaining -= armorLoss;
      }
    }

    return new DamageResolution(armorDamage, healthDamage);
  }
}
