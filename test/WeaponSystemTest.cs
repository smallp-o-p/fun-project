using FunProject.Combatants;
using FunProject.Stats;
using FunProject.Weapons;
using GdUnit4;
using Godot;
using Godot.Collections;
using System;

[TestSuite]
[RequireGodotRuntime]
public class WeaponSystemTest
{
  private WeaponData MakeWeaponData() => new()
  {
    DamageStat = new DamageStat { BaseValue = 10 },
    CriticalChanceStat = new CriticalChanceStat { BaseValue = 5 },
    RangeStat = new RangeStat { BaseValue = 1 },
  };

  private FirearmWeaponData MakeFirearmWeaponData() => new()
  {
    DamageStat = new DamageStat { BaseValue = 10 },
    CriticalChanceStat = new CriticalChanceStat { BaseValue = 5 },
    RangeStat = new RangeStat { BaseValue = 1 },
    AmmunitionStat = new AmmunitionStat { BaseValue = 12 },
    DefaultAmmoData = new AmmunitionData(),
    ModSlotCount = 1
  };

  [TestCase(TestName = "Stat instances work")]
  public void StatInstancesWork()
  {
    var dmg = new DamageStat { BaseValue = 25 };
    Assert.Equal(25, dmg.BaseValue);
  }

  [TestCase(TestName = "MeleeWeapon constructed from RangedWeaponData")]
  public void MeleeWeaponConstructedFromRangedWeaponData()
  {
    var data = new FirearmWeaponData
    {
      Name = "Fists",
      DamageElement = DamageElement.Kinetic,
      DamageStat = new DamageStat { BaseValue = 10 },
      CriticalChanceStat = new CriticalChanceStat { BaseValue = 5 },
      RangeStat = new RangeStat { BaseValue = 1 },
      ModSlotCount = 0,
    };
    var weapon = new MeleeWeapon(data);

    Assert.Equal("Fists", weapon.WeaponName);
    Assert.Equal(DamageElement.Kinetic, weapon.DamageElement);
    Assert.Equal(10, weapon.GetDamageStat().BaseValue);
    Assert.Equal(5, weapon.GetCritChanceStat().BaseValue);
    Assert.Equal(0, weapon.NumModslots());
  }

  [TestCase(TestName = "FirearmWeapon constructed from FirearmWeaponData")]
  public void FirearmWeaponConstructedFromFirearmWeaponData()
  {
    var data = new FirearmWeaponData
    {
      DamageStat = new DamageStat { BaseValue = 15 },
      CriticalChanceStat = new CriticalChanceStat { BaseValue = 10 },
      RangeStat = new RangeStat { BaseValue = 20 },
      DefaultAmmoData = new AmmunitionData(),
      AmmunitionStat = new AmmunitionStat { BaseValue = 12 },
      ModSlotCount = 0,
    };
    var weapon = new FirearmWeapon(data);

    Assert.Equal(12, weapon.GetMagAmmoStat().BaseValue);
    Assert.Equal(FirearmArchetype.Pistol, weapon.Archetype);
  }

  [TestCase(TestName = "MeleeWeapon stats dictionary has no ammo entry")]
  public void MeleeWeaponStatsDictionaryHasNoAmmoEntry()
  {
    var weapon = new MeleeWeapon(MakeWeaponData());
    Assert.False(weapon.TryGetStat<AmmunitionStat>(out _));
  }

  [TestCase(TestName = "Weapon generic stat lookup returns concrete stats")]
  public void WeaponGenericStatLookupReturnsConcreteStats()
  {
    var weapon = new MeleeWeapon(MakeWeaponData());

    Assert.Equal(10, weapon.GetStat<DamageStat>().BaseValue);
    Assert.True(weapon.TryGetStat<RangeStat>(out var range));
    Assert.Equal(1, range.BaseValue);
    Assert.False(weapon.TryGetStat<HealthStat>(out _));
  }

  [TestCase(TestName = "Weapon Type-based stat lookup returns stat instances")]
  public void WeaponTypeBasedStatLookupReturnsStatInstances()
  {
    var weapon = new FirearmWeapon(MakeFirearmWeaponData());

    Assert.True(weapon.TryGetStat(typeof(AmmunitionStat), out var ammoStat));
    Assert.Equal(12, ammoStat.BaseValue);
    Assert.True(ammoStat is AmmunitionStat);
    Assert.False(weapon.TryGetStat(typeof(HealthStat), out _));
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
    Assert.True(combatant.TryGetStat(typeof(BaseArmorStat), out var armorStat));
    Assert.Equal(3, armorStat.BaseValue);
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
    var mod = new DamageEquippableStatMod();
    slot.Equip(mod);
    Assert.True(slot.HasMod);
    var returned = slot.Unequip();
    Assert.Equal(mod, returned);
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
    var mod = new DamageEquippableStatMod();
    mod.AddModifier(StatModifier.Add(10));
    mod.AddModifier(StatModifier.Multiply(2f));
    var data = MakeWeaponData();
    data.DamageStat.BaseValue = 5;
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
        new DamageStatMod { Modifiers = [StatModifier.Add(5)] },
        new RangeStatMod { Modifiers = [StatModifier.Multiply(3f)] },
      ]
    };
    var weapon = new MeleeWeapon(MakeWeaponData());

    var results = mod.Apply(weapon);

    Assert.Equal(15f, results[typeof(DamageStat)]);
    Assert.Equal(3f, results[typeof(RangeStat)]);
  }

  [TestCase(TestName = "MultiStatMod add remove and clear manage internal list")]
  public void MultiStatModAddRemoveAndClearManageInternalList()
  {
    var mod = new MultiStatMod();
    var damage = new DamageStatMod();
    var range = new RangeStatMod();

    mod.AddStatMod(damage);
    mod.AddStatMod(range);
    Assert.Equal(2, mod.StatMods.Count);
    Assert.True(mod.RemoveStatMod(damage));
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
        new DamageStatMod { Modifiers = [StatModifier.Add(5)] },
        new DamageStatMod { Modifiers = [StatModifier.Multiply(2f)] },
      ]
    };
    var weapon = new MeleeWeapon(MakeWeaponData());

    Assert.Throws<InvalidOperationException>(() => mod.Apply(weapon));
  }

  [TestCase(TestName = "Weapon with mod slot produces correct damage")]
  public void WeaponWithModSlotProducesCorrectDamage()
  {
    var data = MakeFirearmWeaponData();
    var weapon = new FirearmWeapon(data);
    weapon.GetModSlots()[0].Equip(new DamageEquippableStatMod
    {
      Name = "+20% Damage",
      Modifiers =
      [
        StatModifier.Multiply(1.2f),
        StatModifier.Add(5),
      ]
    });

    var slot = weapon.GetModSlots()[0];
    Assert.Equal((data.DamageStat.BaseValue * 1.2) + 5, slot.EquippedMod!.GetAppliedStat<DamageStat>(weapon));
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
        new DamageStatMod { Modifiers = [StatModifier.Add(2)] },
        new RangeStatMod { Modifiers = [StatModifier.Add(6)] },
      ]
    });

    var slot = weapon.GetModSlots()[0];
    Assert.Equal(data.DamageStat.BaseValue + 2, slot.EquippedMod!.GetAppliedStat<DamageStat>(weapon));
    Assert.Equal(data.RangeStat.BaseValue + 6, slot.EquippedMod!.GetAppliedStat<RangeStat>(weapon));
  }

  [TestCase(TestName = "All damage elements are valid enum values")]
  public void AllDamageElementsAreValidEnumValues()
  {
    var elements = Enum.GetValues<DamageElement>();
    Assert.Equal(4, elements.Length);
    Assert.Equal(DamageElement.Kinetic, (DamageElement)0);
  }

  [TestCase(TestName = "Weapon can be assigned each damage element")]
  public void WeaponCanBeAssignedEachDamageElement()
  {
    foreach (DamageElement element in Enum.GetValues<DamageElement>())
    {
      var data = MakeWeaponData();
      data.DamageElement = element;
      var weapon = new MeleeWeapon(data);
      Assert.Equal(element, weapon.DamageElement);
    }
  }

  [TestCase(TestName = "All firearm archetypes are valid enum values")]
  public void AllFirearmArchetypesAreValidEnumValues()
  {
    var archetypes = Enum.GetValues<FirearmArchetype>();
    Assert.Equal(4, archetypes.Length);
    Assert.Equal(FirearmArchetype.Pistol, (FirearmArchetype)0);
  }

  [TestCase(TestName = "Weapon helper methods return correct stats")]
  public void WeaponHelperMethodsReturnCorrectStats()
  {
    var data = MakeWeaponData();
    data.DamageStat.BaseValue = 42;
    data.CriticalChanceStat.BaseValue = 99;
    data.RangeStat.BaseValue = 7;
    var weapon = new MeleeWeapon(data);

    Assert.Equal(42, weapon.GetDamageStat().BaseValue);
    Assert.Equal(99, weapon.GetCritChanceStat().BaseValue);
    Assert.Equal(7, weapon.GetRangeStat().BaseValue);
  }

  [TestCase(TestName = "EquippableStatMod AddModifier adds to internal list")]
  public void EquippableStatModAddModifierAddsToInternalList()
  {
    var mod = new DamageEquippableStatMod();
    mod.AddModifier(StatModifier.Add(5));
    Assert.Equal(1, mod.Modifiers.Count);
  }

  [TestCase(TestName = "EquippableStatMod RemoveModifier removes specific modifier")]
  public void EquippableStatModRemoveModifierRemovesSpecificModifier()
  {
    var mod = new DamageEquippableStatMod();
    var add = StatModifier.Add(5);
    mod.AddModifier(add);
    Assert.True(mod.RemoveModifier(add));
    Assert.Equal(0, mod.Modifiers.Count);
  }

  [TestCase(TestName = "EquippableStatMod RemoveModifier returns false for non-existent")]
  public void EquippableStatModRemoveModifierReturnsFalseForNonExistent()
  {
    var mod = new DamageEquippableStatMod();
    Assert.False(mod.RemoveModifier(StatModifier.Add(5)));
  }

  [TestCase(TestName = "EquippableStatMod ClearModifiers clears all")]
  public void EquippableStatModClearModifiersClearsAll()
  {
    var mod = new DamageEquippableStatMod();
    mod.AddModifier(StatModifier.Add(5));
    mod.AddModifier(StatModifier.Multiply(2f));
    mod.ClearModifiers();
    Assert.Equal(0, mod.Modifiers.Count);
  }

  [TestCase(TestName = "EquippableStatMod with no modifiers returns base value")]
  public void EquippableStatModWithNoModifiersReturnsBaseValue()
  {
    var mod = new DamageEquippableStatMod();
    var data = MakeWeaponData();
    data.DamageStat.BaseValue = 42;
    Assert.Equal(42f, mod.ApplyToTarget(new MeleeWeapon(data)));
  }

  [TestCase(TestName = "Multiple mod slots with different stat targets")]
  public void MultipleModSlotsWithDifferentStatTargets()
  {
    var data = MakeFirearmWeaponData();
    data.ModSlotCount = 2;

    var weapon = new FirearmWeapon(data);
    weapon.GetModSlots()[0].EquippedMod = new RangeEquippableStatMod
    {
      Modifiers = [StatModifier.Add(10)],
    };
    weapon.GetModSlots()[1].EquippedMod = new DamageEquippableStatMod
    {
      Modifiers = [StatModifier.Multiply(1.5f)],
    };

    Assert.Equal(data.RangeStat.BaseValue + 10, weapon.GetModSlots()[0].EquippedMod!.GetAppliedStat<RangeStat>(weapon));
    Assert.Equal(data.DamageStat.BaseValue * 1.5, weapon.GetModSlots()[1].EquippedMod!.GetAppliedStat<DamageStat>(weapon));
  }

  [TestCase(TestName = "Multiply by zero zeroes the value")]
  public void MultiplyByZeroZeroesTheValue()
  {
    var weapon = new MeleeWeapon(MakeWeaponData());
    var mod = new DamageEquippableStatMod
    {
      Modifiers = [StatModifier.Multiply(0f)],
    };
    Assert.Equal(0f, mod.ApplyToTarget(weapon));
  }

  [TestCase(TestName = "Add then CapMax chain")]
  public void AddThenCapMaxChain()
  {
    var weapon = new MeleeWeapon(MakeWeaponData());
    var mod = new DamageEquippableStatMod
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
    data.DamageStat.BaseValue = 5;
    var weapon = new MeleeWeapon(data);
    var mod = new DamageEquippableStatMod
    {
      Modifiers =
      [
        StatModifier.Add(-50),
        StatModifier.CapMin(0),
      ],
    };
    Assert.Equal(0f, mod.ApplyToTarget(weapon));
  }

  [TestCase(TestName = "Ammunition struct copies data from AmmunitionData")]
  public void AmmunitionStructCopiesDataFromAmmunitionData()
  {
    var ammoData = new AmmunitionData { Name = "AP Round", Description = "Armor-piercing ammo" };
    var ammo = new Ammunition(ammoData);
    Assert.Equal("AP Round", ammo.AmmoName);
    Assert.Equal("Armor-piercing ammo", ammo.AmmoDescription);
  }

  [TestCase(TestName = "FirearmWeapon initializes AmmoType from DefaultAmmoData")]
  public void FirearmWeaponInitializesAmmoTypeFromDefaultAmmoData()
  {
    var data = new FirearmWeaponData
    {
      DefaultAmmoData = new AmmunitionData { Name = "Standard", Description = "Standard issue rounds" },
      DamageStat = new DamageStat { BaseValue = 10 },
      CriticalChanceStat = new CriticalChanceStat { BaseValue = 5 },
      RangeStat = new RangeStat { BaseValue = 15 },
      AmmunitionStat = new AmmunitionStat { BaseValue = 12 },
      ModSlotCount = 0,
    };
    var weapon = new FirearmWeapon(data);
    Assert.Equal("Standard", weapon.AmmoType.AmmoName);
    Assert.Equal("Standard issue rounds", weapon.AmmoType.AmmoDescription);
  }

  [TestCase(TestName = "FirearmWeapon AmmoType can be reassigned")]
  public void FirearmWeaponAmmoTypeCanBeReassigned()
  {
    var weapon = new FirearmWeapon(MakeFirearmWeaponData());
    var newAmmo = new Ammunition(new AmmunitionData { Name = "Incendiary" });
    weapon.AmmoType = newAmmo;
    Assert.Equal("Incendiary", weapon.AmmoType.AmmoName);
  }

  [TestCase(TestName = "Ammunition struct inherits StatModifier array from AmmunitionData")]
  public void AmmunitionStructInheritsStatModifierArrayFromAmmunitionData()
  {
    var ammoData = new AmmunitionData
    {
      Name = "+Damage Ammo",
      Modifiers =
      [
        new DamageStatMod
        {
          Modifiers = [StatModifier.Add(10)]
        }
      ]
    };
    var ammo = new Ammunition(ammoData);
    Assert.Equal(1, ammo.statModifiers.Count);
  }

  [TestCase(TestName = "AmmunitionedWeaponData holds AmmunitionStat and DefaultAmmoData")]
  public void AmmunitionedWeaponDataHoldsAmmunitionStatAndDefaultAmmoData()
  {
    var data = new AmmunitionedWeaponData
    {
      AmmunitionStat = new AmmunitionStat { BaseValue = 6 },
      DefaultAmmoData = new AmmunitionData { Name = "Test Ammo" },
      DamageStat = new DamageStat { BaseValue = 20 },
      CriticalChanceStat = new CriticalChanceStat { BaseValue = 10 },
      RangeStat = new RangeStat { BaseValue = 5 },
    };
    Assert.Equal(6, data.AmmunitionStat.BaseValue);
    Assert.Equal("Test Ammo", data.DefaultAmmoData.Name);
  }
}
