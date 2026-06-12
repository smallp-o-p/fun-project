using FunProject.Battle;
using FunProject.Core;
using FunProject.Weapons;
using GdUnit4;
using System;

[TestSuite]
[RequireGodotRuntime]
public class DamageResolverTest
{
  [TestCase(TestName = "No armor sends the whole bundle to health")]
  public void NoArmorSendsAllToHealth()
  {
    var resolution = DamageResolver.Resolve(
      [new Damage(5, Element.Kinetic), new Damage(3, Element.Thermal)], None);

    Assert.Equal(new DamageResolution(0, 8), resolution);
  }

  [TestCase(TestName = "Armor absorbs fully with no health spill")]
  public void ArmorAbsorbsFully()
  {
    var resolution = DamageResolver.Resolve(
      [new Damage(4, Element.Thermal)], Some(new ArmorState(10, Element.Kinetic)));

    Assert.Equal(new DamageResolution(4, 0), resolution);
  }

  [TestCase(TestName = "Health spill is computed from armor before the hit")]
  public void SpillComputedFromArmorBeforeHit()
  {
    var resolution = DamageResolver.Resolve(
      [new Damage(8, Element.Thermal)], Some(new ArmorState(5, Element.Kinetic)));

    Assert.Equal(new DamageResolution(5, 3), resolution);
  }

  [TestCase(TestName = "Element match strips armor at 1.5x floored")]
  public void ElementMatchStripsAtOnePointFiveFloored()
  {
    // 10 matched -> 15 vs 15 armor: exactly consumed, no spill.
    Assert.Equal(new DamageResolution(15, 0), DamageResolver.Resolve(
      [new Damage(10, Element.Thermal)], Some(new ArmorState(15, Element.Thermal))));

    // 5 matched -> floor(7.5) = 7 vs 8 armor.
    Assert.Equal(new DamageResolution(7, 0), DamageResolver.Resolve(
      [new Damage(5, Element.Thermal)], Some(new ArmorState(8, Element.Thermal))));
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

  [TestCase(TestName = "Earlier packets deplete the armor seen by later packets")]
  public void EarlierPacketsDepleteArmorForLaterPackets()
  {
    // Packet 1 (6 Thermal, matched): armor loses min(10, 9) = 9, no spill. Remaining 1.
    // Packet 2 (4 Kinetic): spill max(0, 4 - 1) = 3, armor loses 1.
    var resolution = DamageResolver.Resolve(
      [new Damage(6, Element.Thermal), new Damage(4, Element.Kinetic)],
      Some(new ArmorState(10, Element.Thermal)));

    Assert.Equal(new DamageResolution(10, 3), resolution);
  }

  [TestCase(TestName = "Zero and negative packets are ignored")]
  public void ZeroAndNegativePacketsIgnored()
  {
    var resolution = DamageResolver.Resolve(
      [new Damage(0, Element.Kinetic), new Damage(-3, Element.Thermal)],
      Some(new ArmorState(10, Element.Kinetic)));

    Assert.Equal(new DamageResolution(0, 0), resolution);
  }

  [TestCase(TestName = "Depleted armor passes everything to health")]
  public void DepletedArmorPassesThrough()
  {
    var resolution = DamageResolver.Resolve(
      [new Damage(6, Element.Kinetic)], Some(new ArmorState(0, Element.Kinetic)));

    Assert.Equal(new DamageResolution(0, 6), resolution);
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
    Assert.Equal(new PacketResolution(9, 0), packets[0]);
    Assert.Equal(new PacketResolution(1, 3), packets[1]);
  }

  [TestCase(TestName = "ResolvePackets keeps zero and negative packets aligned")]
  public void ResolvePacketsKeepsAlignmentForIgnoredPackets()
  {
    var packets = DamageResolver.ResolvePackets(
      [new Damage(0, Element.Kinetic), new Damage(5, Element.Kinetic)],
      Some(new ArmorState(2, Element.Thermal)));

    Assert.Equal(2, packets.Count);
    Assert.Equal(new PacketResolution(0, 0), packets[0]);
    Assert.Equal(new PacketResolution(2, 3), packets[1]);
  }
}
