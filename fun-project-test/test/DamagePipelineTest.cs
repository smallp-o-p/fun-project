using FunProject.Core;
using FunProject.Items.Capabilities;
using FunProject.Stats;
using FunProject.Weapons;
using GdUnit4;
using System;

[TestSuite]
[RequireGodotRuntime]
public class DamagePipelineTest
{
  [TestCase(TestName = "DamagePacketData derives amount from base damage with rounding")]
  public void PacketDerivesAmountFromBaseDamage()
  {
    Assert.Equal(new Damage(4, Element.Thermal),
      new DamagePacketData { Element = Element.Thermal, Multiplier = 0.7f }.Derive(6));
    Assert.Equal(new Damage(2, Element.Electrical),
      new DamagePacketData { Element = Element.Electrical, Multiplier = 0.3f }.Derive(6));
    Assert.Equal(new Damage(2, Element.Kinetic),
      new DamagePacketData { Element = Element.Kinetic, Multiplier = 0.5f }.Derive(3)); // 1.5 rounds away from zero, truncation would give 1
  }

  [TestCase(TestName = "DamagePacketData defaults to Kinetic at full multiplier")]
  public void PacketDefaultsToKineticFullMultiplier()
  {
    Assert.Equal(new Damage(5, Element.Kinetic), new DamagePacketData().Derive(5));
  }

  [TestCase(TestName = "PacketModifier scaling all elements folds ops over every packet")]
  public void UniformFoldsOpsOverEveryPacket()
  {
    var mod = new DamageBundleMod { PacketModifiers = [new PacketModifier { AffectAllElements = true, Ops = [StatModifier.Add(2), StatModifier.Multiply(2f)] }] };

    var result = mod.Apply(
      [new Damage(4, Element.Kinetic), new Damage(3, Element.Thermal)],
      new DamageEmissionContext(4));

    Assert.Equal(2, result.Count);
    Assert.Equal(new Damage(12, Element.Kinetic), result[0]);
    Assert.Equal(new Damage(10, Element.Thermal), result[1]);
  }

  [TestCase(TestName = "PacketModifier element filter only touches matching packets")]
  public void ElementFilterOnlyTouchesMatchingPackets()
  {
    var mod = new DamageBundleMod
    {
      PacketModifiers = [new PacketModifier { Element = Element.Thermal, Ops = [StatModifier.Add(5)] }],
    };

    var result = mod.Apply(
      [new Damage(4, Element.Kinetic), new Damage(3, Element.Thermal)],
      new DamageEmissionContext(4));

    Assert.Equal(new Damage(4, Element.Kinetic), result[0]);
    Assert.Equal(new Damage(8, Element.Thermal), result[1]);
  }

  [TestCase(TestName = "DamageBundleMod AddedPackets appends a packet derived from base damage")]
  public void AddPacketAppendsDerivedPacket()
  {
    var mod = new DamageBundleMod
    {
      AddedPackets = [new DamagePacketData { Element = Element.Chem, Multiplier = 0.5f }],
    };

    var result = mod.Apply([new Damage(6, Element.Kinetic)], new DamageEmissionContext(6));

    Assert.Equal(2, result.Count);
    Assert.Equal(new Damage(3, Element.Chem), result[1]);
  }

  [TestCase(TestName = "DamageBundleMod with a null added packet throws")]
  public void AddPacketWithoutPacketThrows()
  {
    var mod = new DamageBundleMod { AddedPackets = [null] };
    Assert.Throws<ArgumentNullException>(
      () => mod.Apply([new Damage(6, Element.Kinetic)], new DamageEmissionContext(6)));
  }

  [TestCase(TestName = "PacketModifier remove drops all matching packets")]
  public void RemoveElementRemovesAllMatchingPackets()
  {
    var mod = new DamageBundleMod { PacketModifiers = [new PacketModifier { Element = Element.Kinetic, Remove = true }] };

    var result = mod.Apply(
      [new Damage(4, Element.Kinetic), new Damage(2, Element.Thermal), new Damage(1, Element.Kinetic)],
      new DamageEmissionContext(4));

    Assert.Equal(1, result.Count);
    Assert.Equal(new Damage(2, Element.Thermal), result[0]);
  }

  [TestCase(TestName = "DamageBundleEquippableMod resolves no stats and mounts in a mod slot")]
  public void DamageBundleEquippableModResolvesNoStatsAndMounts()
  {
    var mod = new DamageBundleEquippableMod
    {
      Name = "Test Damage Mod",
      BundleMods = [new DamageBundleMod { PacketModifiers = [new PacketModifier { AffectAllElements = true, Ops = [StatModifier.Add(1)] }] }],
    };

    var slot = new ModSlot();
    slot.Equip(mod);

    Assert.True(slot.HasMod);
    Assert.Equal(1, mod.BundleMods.Count);
    Assert.False(mod.StatContributions.AsValueEnumerable().Any());
  }

  [TestCase(TestName = "EmitDamage derives the frame's packets from base damage")]
  public void EmitDamageDerivesFramePackets()
  {
    var frame = TestData.MakeFrame((Element.Thermal, 0.7f), (Element.Electrical, 0.3f));
    var weapon = new FirearmWeapon(TestData.MakeFirearmWeaponData(damage: 6, frame: frame));

    var bundle = weapon.EmitDamage();

    Assert.Equal(2, bundle.Count);
    Assert.Equal(new Damage(4, Element.Thermal), bundle[0]);
    Assert.Equal(new Damage(2, Element.Electrical), bundle[1]);
  }

  [TestCase(TestName = "EmitDamage applies slot bundle mods in slot order then array order")]
  public void EmitDamageAppliesSlotModsInOrder()
  {
    var weapon = new FirearmWeapon(TestData.MakeFirearmWeaponData(damage: 4, frame: TestData.MakeFrame(), modSlots: 1));
    weapon.GetModSlots()[0].Equip(new DamageBundleEquippableMod
    {
      BundleMods =
      [
        new DamageBundleMod { PacketModifiers = [new PacketModifier { AffectAllElements = true, Ops = [StatModifier.Add(2)] }] },
        new DamageBundleMod { PacketModifiers = [new PacketModifier { AffectAllElements = true, Ops = [StatModifier.Multiply(2f)] }] },
      ],
    });

    // (4 + 2) * 2 = 12; reversed order would give 4 * 2 + 2 = 10.
    Assert.Equal(new Damage(12, Element.Kinetic), weapon.EmitDamage()[0]);
  }

  [TestCase(TestName = "EmitDamage applies ammunition bundle mods after slot mods")]
  public void EmitDamageAppliesAmmoModsAfterSlotMods()
  {
    var ammo = new Ammunition
    {
      DamageMods = [new DamageBundleMod { PacketModifiers = [new PacketModifier { AffectAllElements = true, Ops = [StatModifier.Add(1)] }] }],
    };
    var weapon = new FirearmWeapon(TestData.MakeFirearmWeaponData(damage: 4, frame: TestData.MakeFrame(), ammo: ammo, modSlots: 1));
    weapon.GetModSlots()[0].Equip(new DamageBundleEquippableMod
    {
      BundleMods = [new DamageBundleMod { PacketModifiers = [new PacketModifier { AffectAllElements = true, Ops = [StatModifier.Multiply(2f)] }] }],
    });

    // slots first: 4 * 2 = 8, then ammo: 8 + 1 = 9. Ammo-first would give (4 + 1) * 2 = 10.
    Assert.Equal(new Damage(9, Element.Kinetic), weapon.EmitDamage()[0]);
  }

  [TestCase(TestName = "EmitDamage drops packets at or below zero only at emission")]
  public void EmitDamageDropsNonPositivePacketsAtEmissionOnly()
  {
    var weapon = new FirearmWeapon(TestData.MakeFirearmWeaponData(damage: 4, frame: TestData.MakeFrame(), modSlots: 1));
    weapon.GetModSlots()[0].Equip(new DamageBundleEquippableMod
    {
      BundleMods =
      [
        new DamageBundleMod { PacketModifiers = [new PacketModifier { AffectAllElements = true, Ops = [StatModifier.Add(-10)] }] },  // 4 -> -6 mid-pipeline
        new DamageBundleMod { PacketModifiers = [new PacketModifier { AffectAllElements = true, Ops = [StatModifier.Add(20)] }] },   // -6 -> 14: restored
      ],
    });

    Assert.Equal(new Damage(14, Element.Kinetic), weapon.EmitDamage()[0]);
  }

  [TestCase(TestName = "EmitDamage drops a packet zeroed at the end of the pipeline")]
  public void EmitDamageDropsZeroedPacket()
  {
    var frame = TestData.MakeFrame((Element.Kinetic, 1f), (Element.Thermal, 1f));
    var weapon = new FirearmWeapon(TestData.MakeFirearmWeaponData(damage: 4, frame: frame, modSlots: 1));
    weapon.GetModSlots()[0].Equip(new DamageBundleEquippableMod
    {
      BundleMods = [new DamageBundleMod { PacketModifiers = [new PacketModifier { Element = Element.Kinetic, Ops = [StatModifier.Multiply(0f)] }] }],
    });

    var bundle = weapon.EmitDamage();

    Assert.Equal(1, bundle.Count);
    Assert.Equal(new Damage(4, Element.Thermal), bundle[0]);
  }

  [TestCase(TestName = "Weapon construction rejects a missing or empty frame")]
  public void WeaponConstructionRejectsMissingOrEmptyFrame()
  {
    var missingFrame = TestData.MakeFirearmWeaponData(damage: 4);
    missingFrame.Frame = null;
    Assert.Throws<InvalidOperationException>(() => new FirearmWeapon(missingFrame));
    Assert.Throws<InvalidOperationException>(
      () => new FirearmWeapon(TestData.MakeFirearmWeaponData(damage: 4, frame: new WeaponFrameData { Packets = [] })));
  }

  [TestCase(TestName = "Stat mods on other stats leave the emitted bundle untouched")]
  public void StatModsOnOtherStatsLeaveBundleUntouched()
  {
    var weapon = new FirearmWeapon(TestData.MakeFirearmWeaponData(damage: 4, frame: TestData.MakeFrame(), modSlots: 1));
    weapon.GetModSlots()[0].Equip(new MultiStatMod { StatMods = [new RangeStatMod { Modifiers = [StatModifier.Add(10)] }] });

    Assert.Equal(new Damage(4, Element.Kinetic), weapon.EmitDamage()[0]);
  }
}
