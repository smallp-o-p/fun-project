using FunProject.Battle;
using FunProject.Core;
using FunProject.Weapons;
using GdUnit4;
using System;

[TestSuite]
[RequireGodotRuntime]
public class DamageResolverTest
{
  [TestCase(TestName = "Aggregate resolution: absorption, spill, element matching, stun and ignored packets")]
  public void AggregateResolutionTable()
  {
    (string Name, Damage[] Bundle, Option<ArmorState> Armor, DamageResolution Expected)[] rows =
    [
      ("no armor sends the whole bundle to health",
        [new(5, Element.Kinetic), new(3, Element.Thermal)], None, new(0, 8)),
      ("unmatched armor absorbs fully with no spill",
        [new(4, Element.Thermal)], Some(new ArmorState(10, Element.Kinetic)), new(4, 0)),
      ("health spill is computed from armor before the hit",
        [new(8, Element.Thermal)], Some(new ArmorState(5, Element.Kinetic)), new(5, 3)),
      // 10 matched -> 15 vs 15 armor: exactly consumed, no spill.
      ("10 matched against 15 Thermal strips exactly",
        [new(10, Element.Thermal)], Some(new ArmorState(15, Element.Thermal)), new(15, 0)),
      // 5 matched -> floor(7.5) = 7 vs 8 armor.
      ("5 matched against 8 Thermal floors the strip to 7",
        [new(5, Element.Thermal)], Some(new ArmorState(8, Element.Thermal)), new(7, 0)),
      // Packet 1: armor loses min(10, 9) = 9, no spill, remaining 1. Packet 2: spill max(0, 4 - 1) = 3.
      ("earlier packets deplete the armor seen by later packets",
        [new(6, Element.Thermal), new(4, Element.Kinetic)], Some(new ArmorState(10, Element.Thermal)), new(10, 3)),
      ("zero and negative packets are ignored",
        [new(0, Element.Kinetic), new(-3, Element.Thermal)], Some(new ArmorState(10, Element.Kinetic)), new(0, 0)),
      ("stun without armor reaches stun only",
        [new(5, Element.Kinetic, Kind: DamageKind.Stun)], None, new(0, 0, 5)),
      ("stun ignores zero armor",
        [new(5, Element.Kinetic, Kind: DamageKind.Stun)], Some(new ArmorState(0, Element.Kinetic)), new(0, 0, 5)),
      ("mixed packets resolve the same with health first",
        [new(8, Element.Kinetic), new(3, Element.Thermal, Kind: DamageKind.Stun)],
        Some(new ArmorState(5, Element.Kinetic)), new(5, 3, 3)),
      ("mixed packets resolve the same with stun first",
        [new(3, Element.Thermal, Kind: DamageKind.Stun), new(8, Element.Kinetic)],
        Some(new ArmorState(5, Element.Kinetic)), new(5, 3, 3)),
      ("depleted armor passes everything to health",
        [new(6, Element.Kinetic)], Some(new ArmorState(0, Element.Kinetic)), new(0, 6)),
    ];

    foreach (var row in rows)
      Assert.Equal(row.Expected, DamageResolver.Resolve(row.Bundle, row.Armor), row.Name);
  }

  [TestCase]
  public void StunBypassesArmorAndLeavesItForTheNextPacket()
  {
    Damage[] bundle =
    [
      new(8, Element.Kinetic, Kind: DamageKind.Stun),
      new(4, Element.Thermal),
    ];
    var packets = DamageResolver.ResolvePackets(bundle,
      Some(new ArmorState(10, Element.Kinetic)));
    Assert.Equal(new DamageResolution(0, 0, 8), packets[0]);
    Assert.Equal(new DamageResolution(4, 0, 0), packets[1]);
  }

  [TestCase(TestName = "Element match never increases health spill")]
  public void ElementMatchNeverIncreasesSpill()
  {
    var matched = DamageResolver.Resolve(
      [new Damage(10, Element.Thermal)], Some(new ArmorState(5, Element.Thermal)));
    var unmatched = DamageResolver.Resolve(
      [new Damage(10, Element.Thermal)], Some(new ArmorState(5, Element.Kinetic)));

    Assert.Equal(new DamageResolution(5, 5), matched);
    Assert.Equal(matched.HealthDamage, unmatched.HealthDamage);
  }

  [TestCase(TestName = "Negative armor state is rejected")]
  public void NegativeArmorStateIsRejected()
  {
    Assert.Throws<ArgumentOutOfRangeException>(
      () => DamageResolver.Resolve([new Damage(1, Element.Kinetic)], Some(new ArmorState(-1, Element.Kinetic))));
  }

  [TestCase(TestName = "ResolvePackets reports each packet's armor and health split")]
  public void ResolvePacketsReportsPerPacketSplit()
  {
    var packets = DamageResolver.ResolvePackets(
      [new Damage(6, Element.Thermal), new Damage(4, Element.Kinetic)],
      Some(new ArmorState(10, Element.Thermal)));

    Assert.Equal(2, packets.Count);
    Assert.Equal(new DamageResolution(9, 0), packets[0]);
    Assert.Equal(new DamageResolution(1, 3), packets[1]);
  }

  [TestCase(TestName = "ResolvePackets keeps zero and negative packets aligned")]
  public void ResolvePacketsKeepsAlignmentForIgnoredPackets()
  {
    var packets = DamageResolver.ResolvePackets(
      [new Damage(0, Element.Kinetic), new Damage(5, Element.Kinetic)],
      Some(new ArmorState(2, Element.Thermal)));

    Assert.Equal(2, packets.Count);
    Assert.Equal(new DamageResolution(0, 0), packets[0]);
    Assert.Equal(new DamageResolution(2, 3), packets[1]);
  }
}
