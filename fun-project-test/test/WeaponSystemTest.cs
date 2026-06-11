using FunProject.Combatants;
using FunProject.Items.Capabilities;
using FunProject.Stats;
using FunProject.Tests;
using FunProject.Weapons;
using GdUnit4;
using System;

[TestSuite]
[RequireGodotRuntime]
public class WeaponSystemTest
{
  private WeaponData MakeWeaponData() => new()
  {
    Frame = BattleTestFactory.MakeFrame(),
    DamageStat = new DamageStat { BaseValue = 10 },
    CriticalChanceStat = new CriticalChanceStat { BaseValue = 5 },
    RangeStat = new RangeStat { BaseValue = 1 },
  };

  private FirearmWeaponData MakeFirearmWeaponData() => new()
  {
    Frame = BattleTestFactory.MakeFrame(),
    DamageStat = new DamageStat { BaseValue = 10 },
    CriticalChanceStat = new CriticalChanceStat { BaseValue = 5 },
    RangeStat = new RangeStat { BaseValue = 1 },
    AmmunitionStat = new AmmunitionStat { BaseValue = 12 },
    DefaultAmmoData = new Ammunition(),
    Capabilities = [new ModSlotsCapabilityData { SlotCount = 1 }]
  };

  [TestCase(TestName = "MeleeWeapon constructed from RangedWeaponData")]
  public void MeleeWeaponConstructedFromRangedWeaponData()
  {
    var data = new FirearmWeaponData
    {
      Name = "Fists",
      Frame = BattleTestFactory.MakeFrame(),
      DamageStat = new DamageStat { BaseValue = 10 },
      CriticalChanceStat = new CriticalChanceStat { BaseValue = 5 },
      RangeStat = new RangeStat { BaseValue = 1 },
      AmmunitionStat = default,
      DefaultAmmoData = default
    };
    var weapon = new MeleeWeapon(data);

    Assert.Equal("Fists", weapon.WeaponName);
    Assert.Equal(10, weapon.GetDamageStat().BaseValue);
    Assert.Equal(5, weapon.GetCritChanceStat().BaseValue);
    Assert.Equal(0, weapon.NumModslots());
  }

  [TestCase(TestName = "FirearmWeapon constructed from FirearmWeaponData")]
  public void FirearmWeaponConstructedFromFirearmWeaponData()
  {
    var data = new FirearmWeaponData
    {
      Frame = BattleTestFactory.MakeFrame(),
      DamageStat = new DamageStat { BaseValue = 15 },
      CriticalChanceStat = new CriticalChanceStat { BaseValue = 10 },
      RangeStat = new RangeStat { BaseValue = 20 },
      DefaultAmmoData = new Ammunition(),
      AmmunitionStat = new AmmunitionStat { BaseValue = 12 },
    };
    var weapon = new FirearmWeapon(data);

    Assert.Equal(12, weapon.GetMagAmmoStat().BaseValue);
    Assert.Equal(FirearmArchetype.Pistol, weapon.Archetype);
  }

  [TestCase(TestName = "MeleeWeapon stats dictionary has no ammo entry")]
  public void MeleeWeaponStatsDictionaryHasNoAmmoEntry()
  {
    var weapon = new MeleeWeapon(MakeWeaponData());
    Assert.True(weapon.TryGetStat<AmmunitionStat>().IsNone);
  }

  [TestCase(TestName = "Weapon generic stat lookup returns concrete stats")]
  public void WeaponGenericStatLookupReturnsConcreteStats()
  {
    var weapon = new MeleeWeapon(MakeWeaponData());

    Assert.Equal(10, weapon.GetStat<DamageStat>().BaseValue);
    Option<RangeStat> range = weapon.TryGetStat<RangeStat>();
    Assert.True(range.IsSome);
    Assert.Equal(1, range.RequireSome().BaseValue);
    Assert.True(weapon.TryGetStat<HealthStat>().IsNone);
  }

  [TestCase(TestName = "Weapon Type-based stat lookup returns stat instances")]
  public void WeaponTypeBasedStatLookupReturnsStatInstances()
  {
    var weapon = new FirearmWeapon(MakeFirearmWeaponData());

    Option<Stat> ammoStat = weapon.TryGetStat(typeof(AmmunitionStat));
    Assert.True(ammoStat.IsSome);
    Assert.Equal(12, ammoStat.RequireSome().BaseValue);
    Assert.True(ammoStat.RequireSome() is AmmunitionStat);
    Assert.True(weapon.TryGetStat(typeof(HealthStat)).IsNone);
  }

  [TestCase(TestName = "Combatant exposes all concrete stat types through lookup")]
  public void CombatantExposesAllConcreteStatTypesThroughLookup()
  {
    var combatant = new Combatant(new CombatantData
    {
      Name = "Captain",
      HealthStat = new HealthStat { BaseValue = 20 },
      ActionPointsStat = new ActionPointsStat { BaseValue = 4 },
      WillStat = new WillStat { BaseValue = 50 },
      MovementStat = new MovementStat { BaseValue = 12 },
      VisionStat = new VisionStat { BaseValue = 20 },
      AimStat = new AimStat { BaseValue = 65 },
      BaseArmorStat = new BaseArmorStat { BaseValue = 3 },
      ModSlotCount = 0,
    }, new Faction(new FactionData { Name = "City Guard" }));

    Assert.Equal(20, combatant.GetStat<HealthStat>().BaseValue);
    Assert.Equal(20, combatant.GetStat<VisionStat>().BaseValue);
    Assert.Equal(65, combatant.GetStat<AimStat>().BaseValue);
    Option<Stat> armorStat = combatant.TryGetStat(typeof(BaseArmorStat));
    Assert.True(armorStat.IsSome);
    Assert.Equal(3, armorStat.RequireSome().BaseValue);
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
    var mod = new RangeEquippableStatMod();
    slot.Equip(mod);
    Assert.True(slot.HasMod);
    var returned = slot.Unequip();
    Assert.Equal(mod, returned.RequireSome());
    Assert.False(slot.HasMod);
  }

  [TestCase(TestName = "StatModifier Add applies flat bonus")]
  public void StatModifierAddAppliesFlatBonus()
  {
    var mod = StatModifier.Add(10);
    Assert.Equal(20f, mod.Apply(10));
  }

  [TestCase(TestName = "StatModifier Multiply applies multiplier")]
  public void StatModifierMultiplyAppliesMultiplier()
  {
    var mod = StatModifier.Multiply(1.5f);
    Assert.Equal(15f, mod.Apply(10));
  }

  [TestCase(TestName = "StatModifier CapMin prevents value below floor")]
  public void StatModifierCapMinPreventsValueBelowFloor()
  {
    var mod = StatModifier.CapMin(0);
    Assert.Equal(0f, mod.Apply(-10));
    Assert.Equal(5f, mod.Apply(5));
  }

  [TestCase(TestName = "StatModifier CapMax prevents value above ceiling")]
  public void StatModifierCapMaxPreventsValueAboveCeiling()
  {
    var mod = StatModifier.CapMax(100);
    Assert.Equal(100f, mod.Apply(150));
    Assert.Equal(50f, mod.Apply(50));
  }

  [TestCase(TestName = "EquippableStatMod applies modifiers in order")]
  public void EquippableStatModAppliesModifiersInOrder()
  {
    var mod = new RangeEquippableStatMod();
    mod.AddModifier(StatModifier.Add(10));
    mod.AddModifier(StatModifier.Multiply(2f));
    var data = MakeWeaponData();
    data.RangeStat.BaseValue = 5;
    var weapon = new MeleeWeapon(data);

    Assert.Equal(30f, mod.ApplyToTarget(weapon));
  }

  [TestCase(TestName = "EquippableStatMod rejects wrong stat type")]
  public void EquippableStatModRejectsWrongStatType()
  {
    var mod = new HealthEquippableStatMod();
    var weapon = new MeleeWeapon(MakeWeaponData());

    Assert.Throws<InvalidOperationException>(() => mod.ApplyToTarget(weapon));
  }

  [TestCase(TestName = "Concrete stat mods target stats by class without enum metadata")]
  public void ConcreteStatModsTargetStatsByClassWithoutEnumMetadata()
  {
    var mod = new RangeStatMod
    {
      Modifiers = [StatModifier.Add(4)]
    };
    var weapon = new MeleeWeapon(MakeWeaponData());

    Assert.Equal(5f, mod.Apply(weapon));
  }

  [TestCase(TestName = "MultiStatMod applies multiple stat effects")]
  public void MultiStatModAppliesMultipleStatEffects()
  {
    var mod = new MultiStatMod
    {
      StatMods =
      [
        new CriticalChanceStatMod { Modifiers = [StatModifier.Add(5)] },
        new RangeStatMod { Modifiers = [StatModifier.Multiply(3f)] },
      ]
    };
    var weapon = new MeleeWeapon(MakeWeaponData());

    var results = mod.Apply(weapon);

    Assert.Equal(10f, results[typeof(CriticalChanceStat)]);
    Assert.Equal(3f, results[typeof(RangeStat)]);
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

  [TestCase(TestName = "MultiStatMod rejects duplicate target stat types")]
  public void MultiStatModRejectsDuplicateTargetStatTypes()
  {
    var mod = new MultiStatMod
    {
      StatMods =
      [
        new RangeStatMod { Modifiers = [StatModifier.Add(5)] },
        new RangeStatMod { Modifiers = [StatModifier.Multiply(2f)] },
      ]
    };
    var weapon = new MeleeWeapon(MakeWeaponData());

    Assert.Throws<InvalidOperationException>(() => mod.Apply(weapon));
  }

  [TestCase(TestName = "Weapon with mod slot produces correct range")]
  public void WeaponWithModSlotProducesCorrectRange()
  {
    var data = MakeFirearmWeaponData();
    data.RangeStat.BaseValue = 10;
    var weapon = new FirearmWeapon(data);
    weapon.GetModSlots()[0].Equip(new RangeEquippableStatMod
    {
      Name = "+20% Range",
      Modifiers =
      [
        StatModifier.Multiply(1.2f),
        StatModifier.Add(5),
      ]
    });

    var slot = weapon.GetModSlots()[0];
    Assert.Equal((data.RangeStat.BaseValue * 1.2) + 5, slot.EquippedMod.RequireSome().GetAppliedStat<RangeStat>(weapon));
  }

  [TestCase(TestName = "Weapon mod slot can equip multi-stat mod")]
  public void WeaponModSlotCanEquipMultiStatMod()
  {
    var data = MakeFirearmWeaponData();
    var weapon = new FirearmWeapon(data);
    weapon.GetModSlots()[0].Equip(new MultiStatMod
    {
      Name = "Tactical Overhaul",
      StatMods =
      [
        new CriticalChanceStatMod { Modifiers = [StatModifier.Add(2)] },
        new RangeStatMod { Modifiers = [StatModifier.Add(6)] },
      ]
    });

    var slot = weapon.GetModSlots()[0];
    Assert.Equal(data.CriticalChanceStat.BaseValue + 2, slot.EquippedMod.RequireSome().GetAppliedStat<CriticalChanceStat>(weapon));
    Assert.Equal(data.RangeStat.BaseValue + 6, slot.EquippedMod.RequireSome().GetAppliedStat<RangeStat>(weapon));
  }

  [TestCase(TestName = "EquippableStatMod AddModifier adds to internal list")]
  public void EquippableStatModAddModifierAddsToInternalList()
  {
    var mod = new RangeEquippableStatMod();
    mod.AddModifier(StatModifier.Add(5));
    Assert.Equal(1, mod.Modifiers.Count);
  }

  [TestCase(TestName = "EquippableStatMod RemoveModifier removes specific modifier")]
  public void EquippableStatModRemoveModifierRemovesSpecificModifier()
  {
    var mod = new RangeEquippableStatMod();
    var add = StatModifier.Add(5);
    mod.AddModifier(add);
    Assert.True(mod.RemoveModifier(add));
    Assert.Equal(0, mod.Modifiers.Count);
  }

  [TestCase(TestName = "EquippableStatMod RemoveModifier returns false for non-existent")]
  public void EquippableStatModRemoveModifierReturnsFalseForNonExistent()
  {
    var mod = new RangeEquippableStatMod();
    Assert.False(mod.RemoveModifier(StatModifier.Add(5)));
  }

  [TestCase(TestName = "EquippableStatMod ClearModifiers clears all")]
  public void EquippableStatModClearModifiersClearsAll()
  {
    var mod = new RangeEquippableStatMod();
    mod.AddModifier(StatModifier.Add(5));
    mod.AddModifier(StatModifier.Multiply(2f));
    mod.ClearModifiers();
    Assert.Equal(0, mod.Modifiers.Count);
  }

  [TestCase(TestName = "EquippableStatMod with no modifiers returns base value")]
  public void EquippableStatModWithNoModifiersReturnsBaseValue()
  {
    var mod = new RangeEquippableStatMod();
    var data = MakeWeaponData();
    data.RangeStat.BaseValue = 42;
    Assert.Equal(42f, mod.ApplyToTarget(new MeleeWeapon(data)));
  }

  [TestCase(TestName = "Multiple mod slots with different stat targets")]
  public void MultipleModSlotsWithDifferentStatTargets()
  {
    var data = MakeFirearmWeaponData();
    data.Capabilities = [new ModSlotsCapabilityData { SlotCount = 2 }];

    var weapon = new FirearmWeapon(data);
    weapon.GetModSlots()[0].EquippedMod = Some<EquippableMod>(new RangeEquippableStatMod
    {
      Modifiers = [StatModifier.Add(10)],
    });
    weapon.GetModSlots()[1].EquippedMod = Some<EquippableMod>(new CriticalChanceEquippableStatMod
    {
      Modifiers = [StatModifier.Multiply(1.5f)],
    });

    Assert.Equal(data.RangeStat.BaseValue + 10, weapon.GetModSlots()[0].EquippedMod.RequireSome().GetAppliedStat<RangeStat>(weapon));
    Assert.Equal(data.CriticalChanceStat.BaseValue * 1.5, weapon.GetModSlots()[1].EquippedMod.RequireSome().GetAppliedStat<CriticalChanceStat>(weapon));
  }

  [TestCase(TestName = "Multiply by zero zeroes the value")]
  public void MultiplyByZeroZeroesTheValue()
  {
    var weapon = new MeleeWeapon(MakeWeaponData());
    var mod = new RangeEquippableStatMod
    {
      Modifiers = [StatModifier.Multiply(0f)],
    };
    Assert.Equal(0f, mod.ApplyToTarget(weapon));
  }

  [TestCase(TestName = "Add then CapMax chain")]
  public void AddThenCapMaxChain()
  {
    var weapon = new MeleeWeapon(MakeWeaponData());
    var mod = new RangeEquippableStatMod
    {
      Modifiers =
      [
        StatModifier.Add(100),
        StatModifier.CapMax(75),
      ],
    };
    Assert.Equal(75f, mod.ApplyToTarget(weapon));
  }

  [TestCase(TestName = "CapMin prevents negative result")]
  public void CapMinPreventsNegativeResult()
  {
    var data = MakeWeaponData();
    data.RangeStat.BaseValue = 5;
    var weapon = new MeleeWeapon(data);
    var mod = new RangeEquippableStatMod
    {
      Modifiers =
      [
        StatModifier.Add(-50),
        StatModifier.CapMin(0),
      ],
    };
    Assert.Equal(0f, mod.ApplyToTarget(weapon));
  }

}
