#nullable disable warnings
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

  [TestCase]
  public void DerivationAndModifiersPreserveStunKind()
  {
    var frame = new WeaponFrameData
    {
      Packets =
      [
        new DamagePacketData { Element = Element.Electrical, Kind = DamageKind.Stun, Multiplier = 2f },
        new DamagePacketData { Element = Element.Thermal, Kind = DamageKind.Stun, Multiplier = 1f },
      ],
    };
    var weapon = TestData.MakeWeapon("Stunner", damage: 3, frame: frame);
    var emitted = weapon.EmitDamage();
    var modifier = new PacketModifier
    {
      Element = Element.Electrical,
      Ops = [StatModifier.Add(2), StatModifier.Multiply(2f)],
    };

    var modified = modifier.Apply(emitted).AsValueEnumerable().ToArray();

    // The matching packet becomes (6 + 2) * 2; the other element passes through.
    Damage[] expected =
    [
      new(16, Element.Electrical, Kind: DamageKind.Stun),
      new(3, Element.Thermal, Kind: DamageKind.Stun),
    ];
    Assert.True(modified.AsValueEnumerable().SequenceEqual(expected));
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

  private static FirearmWeapon EmissionWeapon(int damage, WeaponFrameData frame = null, Ammunition ammo = null,
    EquippableMod slotMod = null)
  {
    var weapon = new FirearmWeapon(
      TestData.MakeFirearmWeaponData(damage: damage, frame: frame ?? TestData.MakeFrame(), ammo: ammo, modSlots: 1));
    if (slotMod != null)
      weapon.GetModSlots()[0].Equip(slotMod);
    return weapon;
  }

  private static DamageBundleMod BundleMod(params StatModifier[] ops) =>
    new() { PacketModifiers = [new PacketModifier { AffectAllElements = true, Ops = [.. ops] }] };

  [TestCase(TestName = "Emission pipeline: frame derivation, slot/ammo ordering, emission-only filtering, stat isolation")]
  public void EmissionTable()
  {
    (string Name, Func<FirearmWeapon> Make, Damage[] Expected, bool Whole)[] rows =
    [
      ("frame derivation with rounding",
        () => new FirearmWeapon(TestData.MakeFirearmWeaponData(damage: 6,
          frame: TestData.MakeFrame((Element.Thermal, 0.7f), (Element.Electrical, 0.3f)))),
        [new(4, Element.Thermal), new(2, Element.Electrical)], true),
      // (4 + 2) * 2 = 12; reversed order would give 4 * 2 + 2 = 10.
      ("slot bundle mods apply Add then Multiply in order",
        () => EmissionWeapon(4, slotMod: new DamageBundleEquippableMod
        {
          BundleMods = [BundleMod([StatModifier.Add(2)]), BundleMod([StatModifier.Multiply(2f)])],
        }),
        [new(12, Element.Kinetic)], false),
      // Slots first: 4 * 2 = 8, then ammo: 8 + 1 = 9. Ammo-first would give (4 + 1) * 2 = 10.
      ("ammunition bundle mods apply after slot mods",
        () => EmissionWeapon(4,
          ammo: new Ammunition { DamageMods = [BundleMod([StatModifier.Add(1)])] },
          slotMod: new DamageBundleEquippableMod { BundleMods = [BundleMod([StatModifier.Multiply(2f)])] }),
        [new(9, Element.Kinetic)], false),
      // 4 -> -6 mid-pipeline, then -6 -> 14: the intermediate dip must survive, not merely positive final input.
      ("non-positive mid-pipeline packets still filter only at emission",
        () => EmissionWeapon(4, slotMod: new DamageBundleEquippableMod
        {
          BundleMods = [BundleMod([StatModifier.Add(-10)]), BundleMod([StatModifier.Add(20)])],
        }),
        [new(14, Element.Kinetic)], false),
      ("a packet zeroed at the end of the pipeline is dropped",
        () => EmissionWeapon(4, frame: TestData.MakeFrame((Element.Kinetic, 1f), (Element.Thermal, 1f)),
          slotMod: new DamageBundleEquippableMod
          {
            BundleMods = [new DamageBundleMod
            {
              PacketModifiers = [new PacketModifier { Element = Element.Kinetic, Ops = [StatModifier.Multiply(0f)] }],
            }],
          }),
        [new(4, Element.Thermal)], true),
      ("stat mods on other stats leave the bundle untouched",
        () => EmissionWeapon(4, slotMod: new MultiStatMod
        {
          StatMods = [new RangeStatMod { Modifiers = [StatModifier.Add(10)] }],
        }),
        [new(4, Element.Kinetic)], false),
    ];

    foreach (var row in rows)
    {
      Damage[] bundle = [.. row.Make().EmitDamage()];
      if (row.Whole)
      {
        Assert.Equal(row.Expected.Length, bundle.Length, row.Name);
        Assert.True(bundle.AsValueEnumerable().SequenceEqual(row.Expected), row.Name);
      }
      else
        Assert.Equal(row.Expected[0], bundle[0], row.Name);
    }
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
}
