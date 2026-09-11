#nullable disable warnings
using FunProject.Items.Capabilities;
using FunProject.Stats;
using FunProject.Weapons;
using GdUnit4;

[TestSuite]
[RequireGodotRuntime]
public class WeaponSystemTest
{
  [TestCase(TestName = "MeleeWeapon constructed from RangedWeaponData")]
  public void MeleeWeaponConstructedFromRangedWeaponData()
  {
    var data = new FirearmWeaponData
    {
      Name = "Fists",
      Frame = TestData.MakeFrame(),
      DamageStat = new DamageStat { BaseValue = 10 },
      CriticalChanceStat = new CriticalChanceStat { BaseValue = 5 },
      RangeStat = new RangeStat { BaseValue = 1 },
      AmmunitionStat = default,
      DefaultAmmoData = default
    };
    var weapon = new MeleeWeapon(data);

    Assert.Equal("Fists", weapon.ItemName);
    Assert.Equal(10, weapon.GetDamageStat().BaseValue);
    Assert.Equal(5, weapon.GetCritChanceStat().BaseValue);
    Assert.Equal(0, weapon.GetModSlots().Count);
  }

  [TestCase(TestName = "FirearmWeapon constructed from FirearmWeaponData")]
  public void FirearmWeaponConstructedFromFirearmWeaponData()
  {
    var data = new FirearmWeaponData
    {
      Frame = TestData.MakeFrame(),
      DamageStat = new DamageStat { BaseValue = 15 },
      CriticalChanceStat = new CriticalChanceStat { BaseValue = 10 },
      RangeStat = new RangeStat { BaseValue = 20 },
      DefaultAmmoData = new Ammunition(),
      AmmunitionStat = new AmmunitionStat { BaseValue = 12 },
    };
    var weapon = new FirearmWeapon(data);

    Assert.Equal(12, weapon.MagazineSize);
    Assert.Equal(FirearmArchetype.Pistol, weapon.Archetype);
  }

  [TestCase(TestName = "MeleeWeapon stats dictionary has no ammo entry")]
  public void MeleeWeaponStatsDictionaryHasNoAmmoEntry()
  {
    var weapon = new MeleeWeapon(TestData.MakeWeaponData());
    Assert.True(weapon.TryGetStat<AmmunitionStat>().IsNone);
  }

  [TestCase(TestName = "Weapon generic stat lookup returns concrete stats")]
  public void WeaponGenericStatLookupReturnsConcreteStats()
  {
    var weapon = new MeleeWeapon(TestData.MakeWeaponData());

    Assert.Equal(10, weapon.GetStat<DamageStat>().BaseValue);
    Option<RangeStat> range = weapon.TryGetStat<RangeStat>();
    Assert.True(range.IsSome);
    Assert.Equal(2, range.RequireSome().BaseValue);
    Assert.True(weapon.TryGetStat<HealthStat>().IsNone);
  }

  [TestCase(TestName = "Firearm generic stat lookup returns the ammunition stat")]
  public void FirearmGenericStatLookupReturnsAmmunitionStat()
  {
    var weapon = new FirearmWeapon(TestData.MakeFirearmWeaponData(range: 1, ammo: new Ammunition(), modSlots: 1));

    Option<AmmunitionStat> ammoStat = weapon.TryGetStat<AmmunitionStat>();
    Assert.True(ammoStat.IsSome);
    Assert.Equal(12, ammoStat.RequireSome().BaseValue);
    Assert.True(weapon.TryGetStat<HealthStat>().IsNone);
  }

  [TestCase(TestName = "ModSlot starts empty")]
  public void ModSlotStartsEmpty()
  {
    var slot = new ModSlot();
    Assert.False(slot.HasMod);
  }

  [TestCase(TestName = "ModSlot can equip and unequip")]
  public void ModSlotCanEquipAndUnequip()
  {
    var slot = new ModSlot();
    var mod = new MultiStatMod();
    slot.Equip(mod);
    Assert.True(slot.HasMod);
    var returned = slot.Unequip();
    Assert.Equal(mod, returned.RequireSome());
    Assert.False(slot.HasMod);
  }

  [TestCase(TestName = "StatModifier Add applies flat bonus")]
  public void StatModifierAddAppliesFlatBonus() =>
    Assert.Equal(20f, HasStats.Fold(10f, [StatModifier.Add(10)]));

  [TestCase(TestName = "StatModifier Multiply applies multiplier")]
  public void StatModifierMultiplyAppliesMultiplier() =>
    Assert.Equal(15f, HasStats.Fold(10f, [StatModifier.Multiply(1.5f)]));

  [TestCase(TestName = "StatModifier CapMin prevents value below floor")]
  public void StatModifierCapMinPreventsValueBelowFloor() =>
    Assert.Equal(0f, HasStats.Fold(-10f, [StatModifier.CapMin(0)]));

  [TestCase(TestName = "StatModifier CapMax prevents value above ceiling")]
  public void StatModifierCapMaxPreventsValueAboveCeiling() =>
    Assert.Equal(100f, HasStats.Fold(150f, [StatModifier.CapMax(100)]));

  [TestCase(TestName = "Concrete stat mods target stats by class without enum metadata")]
  public void ConcreteStatModsTargetStatsByClassWithoutEnumMetadata()
  {
    var weapon = new MeleeWeapon(TestData.MakeWeaponData());
    StatMod[] sources = [new RangeStatMod { Modifiers = [StatModifier.Add(4)] }];
    Assert.Equal(6f, weapon.Resolve<RangeStat>(sources));
  }

  [TestCase(TestName = "MultiStatMod add remove and clear manage internal list")]
  public void MultiStatModAddRemoveAndClearManageInternalList()
  {
    var mod = new MultiStatMod();
    var crit = new CriticalChanceStatMod();
    var range = new RangeStatMod();

    mod.AddStatMod(crit);
    mod.AddStatMod(range);
    Assert.Equal(2, mod.StatMods.Count);
    Assert.True(mod.RemoveStatMod(crit));
    Assert.Equal(1, mod.StatMods.Count);
    mod.ClearStatMods();
    Assert.Equal(0, mod.StatMods.Count);
  }

  [TestCase(TestName = "MultiStatMod stacks two mods on the same stat")]
  public void MultiStatModStacksSameStat()
  {
    var data = TestData.MakeFirearmWeaponData(range: 1, ammo: new Ammunition(), modSlots: 1);
    data.RangeStat.BaseValue = 10;
    var weapon = new FirearmWeapon(data);
    weapon.GetModSlots()[0].Equip(new MultiStatMod
    {
      StatMods =
      [
        new RangeStatMod { Modifiers = [StatModifier.Add(5)] },
        new RangeStatMod { Modifiers = [StatModifier.PercentAdd(0.5f)] },
      ],
    });
    // (10 + 5) * 1.5 = 22.5
    Assert.Equal(22.5f, weapon.EffectiveStat<RangeStat>());
  }

  [TestCase(TestName = "Weapon with mod slot produces correct effective range")]
  public void WeaponWithModSlotProducesCorrectRange()
  {
    var data = TestData.MakeFirearmWeaponData(range: 1, ammo: new Ammunition(), modSlots: 1);
    data.RangeStat.BaseValue = 10;
    var weapon = new FirearmWeapon(data);
    weapon.GetModSlots()[0].Equip(new MultiStatMod
    {
      Name = "+20% Range",
      StatMods = [new RangeStatMod { Modifiers = [StatModifier.PercentAdd(0.2f), StatModifier.Add(5)] }],
    });
    // bucketed: (10 + 5) * 1.2 = 18
    Assert.Equal(18f, weapon.EffectiveStat<RangeStat>());
  }

  [TestCase(TestName = "Weapon mod slot can equip a multi-stat mod")]
  public void WeaponModSlotCanEquipMultiStatMod()
  {
    var data = TestData.MakeFirearmWeaponData(range: 1, ammo: new Ammunition(), modSlots: 1);
    var weapon = new FirearmWeapon(data);
    weapon.GetModSlots()[0].Equip(new MultiStatMod
    {
      Name = "Tactical Overhaul",
      StatMods =
      [
        new CriticalChanceStatMod { Modifiers = [StatModifier.Add(2)] },
        new RangeStatMod { Modifiers = [StatModifier.Add(6)] },
      ],
    });
    Assert.Equal(data.CriticalChanceStat.BaseValue + 2, weapon.EffectiveStat<CriticalChanceStat>());
    Assert.Equal(data.RangeStat.BaseValue + 6, weapon.EffectiveStat<RangeStat>());
  }

  [TestCase(TestName = "Two slots with different stat targets both apply")]
  public void MultipleModSlotsWithDifferentStatTargets()
  {
    var data = TestData.MakeFirearmWeaponData(range: 1, ammo: new Ammunition(), modSlots: 1);
    data.Capabilities = [new ModSlotsCapabilityData { SlotCount = 2 }];
    var weapon = new FirearmWeapon(data);
    weapon.GetModSlots()[0].Equip(new MultiStatMod { StatMods = [new RangeStatMod { Modifiers = [StatModifier.Add(10)] }] });
    weapon.GetModSlots()[1].Equip(new MultiStatMod { StatMods = [new CriticalChanceStatMod { Modifiers = [StatModifier.PercentAdd(0.5f)] }] });
    Assert.Equal(data.RangeStat.BaseValue + 10, weapon.EffectiveStat<RangeStat>());
    Assert.Equal(data.CriticalChanceStat.BaseValue * 1.5f, weapon.EffectiveStat<CriticalChanceStat>());
  }

}
