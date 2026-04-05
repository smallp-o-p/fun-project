using Godot;
using Godot.Collections;
using System;
using FunProject.Stats;
using FunProject.Weapons;

public partial class WeaponSystemTest : Node
{
  private WeaponData MakeWeaponData() => new()
  {
    DamageStat = new Stat { StatType = StatType.Damage, BaseValue = 10 },
    CriticalChanceStat = new Stat { StatType = StatType.CriticalChance, BaseValue = 5 },
    RangeStat = new Stat { StatType = StatType.Range, BaseValue = 1 },
  };

  private FirearmWeaponData MakeFirearmWeaponData() => new()
  {
    DamageStat = new Stat { StatType = StatType.Damage, BaseValue = 10 },
    CriticalChanceStat = new Stat { StatType = StatType.CriticalChance, BaseValue = 5 },
    RangeStat = new Stat { StatType = StatType.Range, BaseValue = 1 },
    AmmunitionStat = new Stat { StatType = StatType.Ammunition, BaseValue = 12 },
  };

  public override void _Ready()
  {
    T("Stat instances work", () =>
    {
      var dmg = new Stat { StatType = StatType.Damage, BaseValue = 25 };
      Assert.Equal(25, dmg.BaseValue);
      Assert.Equal(StatType.Damage, dmg.StatType);
    });

    T("MeleeWeapon constructed from RangedWeaponData", () =>
    {
      var data = new FirearmWeaponData
      {
        Name = "Fists",
        DamageElement = DamageElement.Kinetic,
        DamageStat = new Stat { StatType = StatType.Damage, BaseValue = 10 },
        CriticalChanceStat = new Stat { StatType = StatType.CriticalChance, BaseValue = 5 },
        RangeStat = new Stat { StatType = StatType.Range, BaseValue = 1 },
        ModSlotCount = 0,
      };
      var m = new MeleeWeapon(data);
      Assert.Equal("Fists", m.WeaponName);
      Assert.Equal(DamageElement.Kinetic, m.DamageElement);
      Assert.Equal(10, m.GetDamageStat().BaseValue);
      Assert.Equal(5, m.GetCritChanceStat().BaseValue);
      Assert.Equal(0, m.NumModslots());
    });

    T("AmmunitionedWeapon constructed from FirearmWeaponData", () =>
    {
      var data = new FirearmWeaponData
      {
        DamageStat = new Stat { StatType = StatType.Damage, BaseValue = 15 },
        CriticalChanceStat = new Stat { StatType = StatType.CriticalChance, BaseValue = 10 },
        RangeStat = new Stat { StatType = StatType.Range, BaseValue = 20 },
        AmmunitionStat = new Stat { StatType = StatType.Ammunition, BaseValue = 12 },
        ModSlotCount = 0,
      };
      var f = new FirearmWeapon(data);

      Assert.Equal(12, f.GetStats()[StatType.Ammunition].BaseValue);
      Assert.Equal(FirearmArchetype.Pistol, f.Archetype);
    });

    T("MeleeWeapon stats dictionary has no ammo entry", () =>
    {
      var data = MakeWeaponData();
      var w = new MeleeWeapon(data);
      Assert.False(w.GetStats().ContainsKey(StatType.Ammunition));
    });

    T("ModSlot starts empty", () =>
    {
      var slot = new ModSlot();
      Assert.False(slot.HasMod);
    });

    T("ModSlot can equip and unequip", () =>
    {
      var slot = new ModSlot();
      var mod = new EquippableStatMod { TargetStat = StatType.Damage };
      slot.Equip(mod);
      Assert.True(slot.HasMod);
      var returned = slot.Unequip();
      Assert.Equal(mod, returned);
      Assert.False(slot.HasMod);
    });

    T("StatModifier_Add applies flat bonus", () =>
    {
      var mod = new StatModifier_Add { Value = 10 };
      Assert.Equal(20, mod.Apply(10));
    });

    T("StatModifier_Multiply applies multiplier", () =>
    {
      var mod = new StatModifier_Multiply { Multiplier = 1.5f };
      Assert.Equal(15, mod.Apply(10));
    });

    T("StatModifier_CapMin prevents value below floor", () =>
    {
      var mod = new StatModifier_CapMin { Min = 0 };
      Assert.Equal(0, mod.Apply(-10));
      Assert.Equal(5, mod.Apply(5));
    });

    T("StatModifier_CapMax prevents value above ceiling", () =>
    {
      var mod = new StatModifier_CapMax { Max = 100 };
      Assert.Equal(100, mod.Apply(150));
      Assert.Equal(50, mod.Apply(50));
    });

    T("EquippableStatMod applies modifiers in order", () =>
    {
      var mod = new EquippableStatMod { TargetStat = StatType.Damage };
      mod.AddModifier(new StatModifier_Add { Value = 10 });
      mod.AddModifier(new StatModifier_Multiply { Multiplier = 2f });
      var data = MakeWeaponData();
      data.DamageStat.BaseValue = 5;
      var weapon = new MeleeWeapon(data);
      Assert.Equal(30f, mod.Apply(weapon));
    });

    T("EquippableStatMod rejects wrong stat type", () =>
    {
      var mod = new EquippableStatMod { TargetStat = StatType.Health };
      var data = MakeWeaponData();
      var weapon = new MeleeWeapon(data);
      Assert.Throws<InvalidOperationException>(() =>
        mod.Apply(weapon));
    });

    T("Weapon with mod slot produces correct damage", () =>
    {
      var data = new FirearmWeaponData
      {
        DamageStat = new Stat { StatType = StatType.Damage, BaseValue = 50 },
        CriticalChanceStat = new Stat { StatType = StatType.CriticalChance, BaseValue = 10 },
        RangeStat = new Stat { StatType = StatType.Range, BaseValue = 25 },
        AmmunitionStat = new Stat { StatType = StatType.Ammunition, BaseValue = 30 },
        ModSlotCount = 1,
      };

      var weapon = new FirearmWeapon(data);
      weapon.GetModSlots()[0].Equip(new EquippableStatMod
      {
        Name = "+20% Damage",
        TargetStat = StatType.Damage,
        Modifiers =
        [
          new StatModifier_Multiply { Multiplier = 1.2f },
          new StatModifier_Add { Value = 5 },
        ]
      });

      var slot = weapon.GetModSlots()[0];
      Assert.Equal(65f, slot.EquippedMod!.Apply(weapon));
    });

    // --- DamageElement coverage ---

    T("All damage elements are valid enum values", () =>
    {
      var elements = Enum.GetValues<DamageElement>();
      Assert.Equal(4, elements.Length);
      Assert.Equal(DamageElement.Kinetic, (DamageElement)0);
    });

    T("Weapon can be assigned each damage element", () =>
    {
      foreach (DamageElement el in Enum.GetValues<DamageElement>())
      {
        var data = MakeWeaponData();
        data.DamageElement = el;
        var w = new MeleeWeapon(data);
        Assert.Equal(el, w.DamageElement);
      }
    });

    // --- FirearmArchetype coverage ---

    T("All firearm archetypes are valid enum values", () =>
    {
      var archetypes = Enum.GetValues<FirearmArchetype>();
      Assert.Equal(4, archetypes.Length);
      Assert.Equal(FirearmArchetype.Pistol, (FirearmArchetype)0);
    });

    T("Each archetype produces correct stat access", () =>
    {
      var data = new FirearmWeaponData
      {
        DamageStat = new Stat { StatType = StatType.Damage, BaseValue = 10 },
        CriticalChanceStat = new Stat { StatType = StatType.CriticalChance, BaseValue = 5 },
        RangeStat = new Stat { StatType = StatType.Range, BaseValue = 15 },
        AmmunitionStat = new Stat { StatType = StatType.Ammunition, BaseValue = 8 },
        ModSlotCount = 0,
      };

      foreach (FirearmArchetype arch in Enum.GetValues<FirearmArchetype>())
      {
        var w = new FirearmWeapon(data);
        w.Archetype = arch;
        Assert.Equal(arch, w.Archetype);
        Assert.Equal(10, w.GetDamageStat().BaseValue);
        Assert.Equal(15, w.GetRangeStat().BaseValue);
        Assert.Equal(5, w.GetCritChanceStat().BaseValue);
        Assert.Equal(8, w.GetStats()[StatType.Ammunition].BaseValue);
      }
    });

    // --- Weapon helper methods ---

    T("Weapon helper methods return correct stats", () =>
    {
      var data = MakeWeaponData();
      data.DamageStat.BaseValue = 42;
      data.CriticalChanceStat.BaseValue = 99;
      data.RangeStat.BaseValue = 7;
      var m = new MeleeWeapon(data);
      Assert.Equal(42, m.GetDamageStat().BaseValue);
      Assert.Equal(99, m.GetCritChanceStat().BaseValue);
      Assert.Equal(7, m.GetRangeStat().BaseValue);
    });

    // --- EquippableStatMod add/remove/clear ---

    T("EquippableStatMod AddModifier adds to internal list", () =>
    {
      var mod = new EquippableStatMod { TargetStat = StatType.Damage };
      mod.AddModifier(new StatModifier_Add { Value = 5 });
      Assert.Equal(1, mod.Modifiers.Count);
    });

    T("EquippableStatMod RemoveModifier removes specific modifier", () =>
    {
      var mod = new EquippableStatMod { TargetStat = StatType.Damage };
      var add = new StatModifier_Add { Value = 5 };
      mod.AddModifier(add);
      Assert.True(mod.RemoveModifier(add));
      Assert.Equal(0, mod.Modifiers.Count);
    });

    T("EquippableStatMod RemoveModifier returns false for non-existent", () =>
    {
      var mod = new EquippableStatMod { TargetStat = StatType.Damage };
      Assert.False(mod.RemoveModifier(new StatModifier_Add { Value = 5 }));
    });

    T("EquippableStatMod ClearModifiers clears all", () =>
    {
      var mod = new EquippableStatMod { TargetStat = StatType.Damage };
      mod.AddModifier(new StatModifier_Add { Value = 5 });
      mod.AddModifier(new StatModifier_Multiply { Multiplier = 2f });
      mod.ClearModifiers();
      Assert.Equal(0, mod.Modifiers.Count);
    });

    T("EquippableStatMod with no modifiers returns base value", () =>
    {
      var mod = new EquippableStatMod { TargetStat = StatType.Damage };
      var data = MakeWeaponData();
      data.DamageStat.BaseValue = 42;
      Assert.Equal(42f, mod.Apply(new MeleeWeapon(data)));
    });

    T("Multiple mod slots with different stat targets", () =>
    {
      var data = new FirearmWeaponData
      {
        DamageStat = new Stat { StatType = StatType.Damage, BaseValue = 20 },
        CriticalChanceStat = new Stat { StatType = StatType.CriticalChance, BaseValue = 10 },
        RangeStat = new Stat { StatType = StatType.Range, BaseValue = 5 },
        AmmunitionStat = new Stat { StatType = StatType.Ammunition, BaseValue = 30 },
        ModSlotCount = 2,
      };

      var weapon = new FirearmWeapon(data);
      weapon.GetModSlots()[0].EquippedMod = new EquippableStatMod
      {
        TargetStat = StatType.Range,
        Modifiers = [new StatModifier_Add { Value = 10 }],
      };
      weapon.GetModSlots()[1].EquippedMod = new EquippableStatMod
      {
        TargetStat = StatType.Damage,
        Modifiers = [new StatModifier_Multiply { Multiplier = 1.5f }],
      };

      Assert.Equal(15f, weapon.GetModSlots()[0].EquippedMod!.Apply(weapon));
      Assert.Equal(30f, weapon.GetModSlots()[1].EquippedMod!.Apply(weapon));
    });

    // --- Modifier edge cases ---

    T("Multiply by zero zeroes the value", () =>
    {
      var weapon = new MeleeWeapon(MakeWeaponData());
      var mod = new EquippableStatMod
      {
        TargetStat = StatType.Damage,
        Modifiers = [new StatModifier_Multiply { Multiplier = 0f }],
      };
      Assert.Equal(0f, mod.Apply(weapon));
    });

    T("Add then CapMax chain", () =>
    {
      var weapon = new MeleeWeapon(MakeWeaponData());
      var mod = new EquippableStatMod
      {
        TargetStat = StatType.Damage,
        Modifiers =
        [
          new StatModifier_Add { Value = 100 },
          new StatModifier_CapMax { Max = 75 },
        ],
      };
      Assert.Equal(75f, mod.Apply(weapon));
    });

    T("CapMin prevents negative result", () =>
    {
      var data = MakeWeaponData();
      data.DamageStat.BaseValue = 5;
      var weapon = new MeleeWeapon(data);
      var mod = new EquippableStatMod
      {
        TargetStat = StatType.Damage,
        Modifiers =
        [
          new StatModifier_Add { Value = -50 },
          new StatModifier_CapMin { Min = 0 },
        ],
      };
      Assert.Equal(0f, mod.Apply(weapon));
    });

    // --- StatType enum coverage ---

    T("All StatType enum values are defined", () =>
    {
      var types = Enum.GetValues<StatType>();
      Assert.Equal(5, types.Length);
      Assert.Equal(StatType.Health, (StatType)0);
    });

    int total = _passed + _failed;
    float pct = total > 0 ? (float)_passed / total * 100f : 0f;
    GD.Print($"Tests passed: {_passed}/{total} ({pct:F0}%)");
    GetTree().Quit(0);
  }

  private int _passed;
  private int _failed;

  private void T(string name, Action body)
  {
    bool ok = true;
    try { body(); } catch (Exception e) { GD.PrintErr($"  FAIL: {name}: {e.Message}"); ok = false; }
    if (ok) { _passed++; GD.Print($"  PASS: {name}"); } else { _failed++; }
  }
}
