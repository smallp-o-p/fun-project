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
    Assert.Equal(new Damage(4, DamageElement.Thermal),
      new DamagePacketData { Element = DamageElement.Thermal, Multiplier = 0.7f }.Derive(6));
    Assert.Equal(new Damage(2, DamageElement.Electrical),
      new DamagePacketData { Element = DamageElement.Electrical, Multiplier = 0.3f }.Derive(6));
    Assert.Equal(new Damage(2, DamageElement.Kinetic),
      new DamagePacketData { Element = DamageElement.Kinetic, Multiplier = 0.5f }.Derive(3)); // 1.5 rounds away from zero, truncation would give 1
  }

  [TestCase(TestName = "DamagePacketData defaults to Kinetic at full multiplier")]
  public void PacketDefaultsToKineticFullMultiplier()
  {
    Assert.Equal(new Damage(5, DamageElement.Kinetic), new DamagePacketData().Derive(5));
  }

  [TestCase(TestName = "UniformDamageBundleMod folds ops over every packet")]
  public void UniformFoldsOpsOverEveryPacket()
  {
    var mod = new UniformDamageBundleMod { Ops = [StatModifier.Add(2), StatModifier.Multiply(2f)] };

    var result = mod.Apply(
      [new Damage(4, DamageElement.Kinetic), new Damage(3, DamageElement.Thermal)],
      new DamageEmissionContext(4));

    Assert.Equal(2, result.Count);
    Assert.Equal(new Damage(12, DamageElement.Kinetic), result[0]);
    Assert.Equal(new Damage(10, DamageElement.Thermal), result[1]);
  }

  [TestCase(TestName = "ElementFilterDamageBundleMod only touches matching packets")]
  public void ElementFilterOnlyTouchesMatchingPackets()
  {
    var mod = new ElementFilterDamageBundleMod
    {
      Element = DamageElement.Thermal,
      Ops = [StatModifier.Add(5)],
    };

    var result = mod.Apply(
      [new Damage(4, DamageElement.Kinetic), new Damage(3, DamageElement.Thermal)],
      new DamageEmissionContext(4));

    Assert.Equal(new Damage(4, DamageElement.Kinetic), result[0]);
    Assert.Equal(new Damage(8, DamageElement.Thermal), result[1]);
  }

  [TestCase(TestName = "AddPacketDamageBundleMod appends a packet derived from base damage")]
  public void AddPacketAppendsDerivedPacket()
  {
    var mod = new AddPacketDamageBundleMod
    {
      Packet = new DamagePacketData { Element = DamageElement.Chem, Multiplier = 0.5f },
    };

    var result = mod.Apply([new Damage(6, DamageElement.Kinetic)], new DamageEmissionContext(6));

    Assert.Equal(2, result.Count);
    Assert.Equal(new Damage(3, DamageElement.Chem), result[1]);
  }

  [TestCase(TestName = "AddPacketDamageBundleMod with no packet authored throws")]
  public void AddPacketWithoutPacketThrows()
  {
    var mod = new AddPacketDamageBundleMod();
    Assert.Throws<ArgumentNullException>(
      () => mod.Apply([new Damage(6, DamageElement.Kinetic)], new DamageEmissionContext(6)));
  }

  [TestCase(TestName = "RemoveElementDamageBundleMod removes all matching packets")]
  public void RemoveElementRemovesAllMatchingPackets()
  {
    var mod = new RemoveElementDamageBundleMod { Element = DamageElement.Kinetic };

    var result = mod.Apply(
      [new Damage(4, DamageElement.Kinetic), new Damage(2, DamageElement.Thermal), new Damage(1, DamageElement.Kinetic)],
      new DamageEmissionContext(4));

    Assert.Equal(1, result.Count);
    Assert.Equal(new Damage(2, DamageElement.Thermal), result[0]);
  }

  [TestCase(TestName = "DamageBundleEquippableMod resolves no stats and mounts in a mod slot")]
  public void DamageBundleEquippableModResolvesNoStatsAndMounts()
  {
    var mod = new DamageBundleEquippableMod
    {
      Name = "Test Damage Mod",
      BundleMods = [new UniformDamageBundleMod { Ops = [StatModifier.Add(1)] }],
    };

    var slot = new ModSlot();
    slot.Equip(mod);

    Assert.True(slot.HasMod);
    Assert.Equal(1, mod.BundleMods.Count);
    Assert.Equal(0, mod.Apply(new FirearmWeapon(MakeFirearmData(damage: 4, BattleTestFactory.MakeFrame()))).Count);
  }

  private static FirearmWeaponData MakeFirearmData(int damage, WeaponFrameData frame, Ammunition ammo = null, int modSlots = 0) => new()
  {
    Name = "Test Firearm",
    Frame = frame,
    DamageStat = new DamageStat { BaseValue = damage },
    RangeStat = new RangeStat { BaseValue = 10 },
    CriticalChanceStat = new CriticalChanceStat { BaseValue = 0 },
    AmmunitionStat = new AmmunitionStat { BaseValue = 12 },
    DefaultAmmoData = ammo,
    Capabilities = modSlots > 0 ? [new ModSlotsCapabilityData { SlotCount = modSlots }] : [],
  };

  [TestCase(TestName = "EmitDamage derives the frame's packets from base damage")]
  public void EmitDamageDerivesFramePackets()
  {
    var frame = BattleTestFactory.MakeFrame((DamageElement.Thermal, 0.7f), (DamageElement.Electrical, 0.3f));
    var weapon = new FirearmWeapon(MakeFirearmData(damage: 6, frame));

    var bundle = weapon.EmitDamage();

    Assert.Equal(2, bundle.Count);
    Assert.Equal(new Damage(4, DamageElement.Thermal), bundle[0]);
    Assert.Equal(new Damage(2, DamageElement.Electrical), bundle[1]);
  }

  [TestCase(TestName = "EmitDamage applies slot bundle mods in slot order then array order")]
  public void EmitDamageAppliesSlotModsInOrder()
  {
    var weapon = new FirearmWeapon(MakeFirearmData(damage: 4, BattleTestFactory.MakeFrame(), modSlots: 1));
    weapon.GetModSlots()[0].Equip(new DamageBundleEquippableMod
    {
      BundleMods =
      [
        new UniformDamageBundleMod { Ops = [StatModifier.Add(2)] },
        new UniformDamageBundleMod { Ops = [StatModifier.Multiply(2f)] },
      ],
    });

    // (4 + 2) * 2 = 12; reversed order would give 4 * 2 + 2 = 10.
    Assert.Equal(new Damage(12, DamageElement.Kinetic), weapon.EmitDamage()[0]);
  }

  [TestCase(TestName = "EmitDamage applies ammunition bundle mods after slot mods")]
  public void EmitDamageAppliesAmmoModsAfterSlotMods()
  {
    var ammo = new Ammunition
    {
      DamageMods = [new UniformDamageBundleMod { Ops = [StatModifier.Add(1)] }],
    };
    var weapon = new FirearmWeapon(MakeFirearmData(damage: 4, BattleTestFactory.MakeFrame(), ammo, modSlots: 1));
    weapon.GetModSlots()[0].Equip(new DamageBundleEquippableMod
    {
      BundleMods = [new UniformDamageBundleMod { Ops = [StatModifier.Multiply(2f)] }],
    });

    // slots first: 4 * 2 = 8, then ammo: 8 + 1 = 9. Ammo-first would give (4 + 1) * 2 = 10.
    Assert.Equal(new Damage(9, DamageElement.Kinetic), weapon.EmitDamage()[0]);
  }

  [TestCase(TestName = "EmitDamage drops packets at or below zero only at emission")]
  public void EmitDamageDropsNonPositivePacketsAtEmissionOnly()
  {
    var weapon = new FirearmWeapon(MakeFirearmData(damage: 4, BattleTestFactory.MakeFrame(), modSlots: 1));
    weapon.GetModSlots()[0].Equip(new DamageBundleEquippableMod
    {
      BundleMods =
      [
        new UniformDamageBundleMod { Ops = [StatModifier.Add(-10)] },  // 4 -> -6 mid-pipeline
        new UniformDamageBundleMod { Ops = [StatModifier.Add(20)] },   // -6 -> 14: restored
      ],
    });

    Assert.Equal(new Damage(14, DamageElement.Kinetic), weapon.EmitDamage()[0]);
  }

  [TestCase(TestName = "EmitDamage drops a packet zeroed at the end of the pipeline")]
  public void EmitDamageDropsZeroedPacket()
  {
    var frame = BattleTestFactory.MakeFrame((DamageElement.Kinetic, 1f), (DamageElement.Thermal, 1f));
    var weapon = new FirearmWeapon(MakeFirearmData(damage: 4, frame, modSlots: 1));
    weapon.GetModSlots()[0].Equip(new DamageBundleEquippableMod
    {
      BundleMods = [new ElementFilterDamageBundleMod { Element = DamageElement.Kinetic, Ops = [StatModifier.Multiply(0f)] }],
    });

    var bundle = weapon.EmitDamage();

    Assert.Equal(1, bundle.Count);
    Assert.Equal(new Damage(4, DamageElement.Thermal), bundle[0]);
  }

  [TestCase(TestName = "Weapon construction rejects a missing or empty frame")]
  public void WeaponConstructionRejectsMissingOrEmptyFrame()
  {
    Assert.Throws<InvalidOperationException>(() => new FirearmWeapon(MakeFirearmData(damage: 4, frame: null)));
    Assert.Throws<InvalidOperationException>(
      () => new FirearmWeapon(MakeFirearmData(damage: 4, new WeaponFrameData { Packets = [] })));
  }

  [TestCase(TestName = "Stat mods on other stats leave the emitted bundle untouched")]
  public void StatModsOnOtherStatsLeaveBundleUntouched()
  {
    var weapon = new FirearmWeapon(MakeFirearmData(damage: 4, BattleTestFactory.MakeFrame(), modSlots: 1));
    weapon.GetModSlots()[0].Equip(new RangeEquippableStatMod { Modifiers = [StatModifier.Add(10)] });

    Assert.Equal(new Damage(4, DamageElement.Kinetic), weapon.EmitDamage()[0]);
  }
}
