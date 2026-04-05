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
    DefaultAmmoData = new AmmunitionData(),
    ModSlotCount = 1
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

    T("FirearmWeapon constructed from FirearmWeaponData", () =>
    {
      var data = new FirearmWeaponData
      {
        DamageStat = new Stat { StatType = StatType.Damage, BaseValue = 15 },
        CriticalChanceStat = new Stat { StatType = StatType.CriticalChance, BaseValue = 10 },
        RangeStat = new Stat { StatType = StatType.Range, BaseValue = 20 },
        DefaultAmmoData = new AmmunitionData(),
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

    T("StatModifier Add applies flat bonus", () =>
    {
      var mod = StatModifier.Add(10);
      Assert.Equal(20f, mod.Apply(10));
    });

    T("StatModifier Multiply applies multiplier", () =>
    {
      var mod = StatModifier.Multiply(1.5f);
      Assert.Equal(15f, mod.Apply(10));
    });

    T("StatModifier CapMin prevents value below floor", () =>
    {
      var mod = StatModifier.CapMin(0);
      Assert.Equal(0f, mod.Apply(-10));
      Assert.Equal(5f, mod.Apply(5));
    });

    T("StatModifier CapMax prevents value above ceiling", () =>
    {
      var mod = StatModifier.CapMax(100);
      Assert.Equal(100f, mod.Apply(150));
      Assert.Equal(50f, mod.Apply(50));
    });

    T("EquippableStatMod applies modifiers in order", () =>
    {
      var mod = new EquippableStatMod { TargetStat = StatType.Damage };
      mod.AddModifier(StatModifier.Add(10));
      mod.AddModifier(StatModifier.Multiply(2f));
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
      var data = MakeFirearmWeaponData();
      var weapon = new FirearmWeapon(data);
      weapon.GetModSlots()[0].Equip(new EquippableStatMod
      {
        Name = "+20% Damage",
        TargetStat = StatType.Damage,
        Modifiers =
        [
          StatModifier.Multiply(1.2f),
          StatModifier.Add(5),
        ]
      });

      var slot = weapon.GetModSlots()[0];
      Assert.Equal((data.DamageStat.BaseValue * 1.2) + 5, slot.EquippedMod!.Apply(weapon));
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
      mod.AddModifier(StatModifier.Add(5));
      Assert.Equal(1, mod.Modifiers.Count);
    });

    T("EquippableStatMod RemoveModifier removes specific modifier", () =>
    {
      var mod = new EquippableStatMod { TargetStat = StatType.Damage };
      var add = StatModifier.Add(5);
      mod.AddModifier(add);
      Assert.True(mod.RemoveModifier(add));
      Assert.Equal(0, mod.Modifiers.Count);
    });

    T("EquippableStatMod RemoveModifier returns false for non-existent", () =>
    {
      var mod = new EquippableStatMod { TargetStat = StatType.Damage };
      Assert.False(mod.RemoveModifier(StatModifier.Add(5)));
    });

    T("EquippableStatMod ClearModifiers clears all", () =>
    {
      var mod = new EquippableStatMod { TargetStat = StatType.Damage };
      mod.AddModifier(StatModifier.Add(5));
      mod.AddModifier(StatModifier.Multiply(2f));
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
      var data = MakeFirearmWeaponData();
      data.ModSlotCount = 2;

      var weapon = new FirearmWeapon(data);
      weapon.GetModSlots()[0].EquippedMod = new EquippableStatMod
      {
        TargetStat = StatType.Range,
        Modifiers = [StatModifier.Add(10)],
      };
      weapon.GetModSlots()[1].EquippedMod = new EquippableStatMod
      {
        TargetStat = StatType.Damage,
        Modifiers = [StatModifier.Multiply(1.5f)],
      };

      Assert.Equal(data.RangeStat.BaseValue + 10, weapon.GetModSlots()[0].EquippedMod!.Apply(weapon));
      Assert.Equal(data.DamageStat.BaseValue * 1.5, weapon.GetModSlots()[1].EquippedMod!.Apply(weapon));
    });

    // --- Modifier edge cases ---

    T("Multiply by zero zeroes the value", () =>
    {
      var weapon = new MeleeWeapon(MakeWeaponData());
      var mod = new EquippableStatMod
      {
        TargetStat = StatType.Damage,
        Modifiers = [StatModifier.Multiply(0f)],
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
          StatModifier.Add(100),
          StatModifier.CapMax(75),
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
          StatModifier.Add(-50),
          StatModifier.CapMin(0),
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

    // --- Ammunition and AmmoType ---

    T("Ammunition struct copies data from AmmunitionData", () =>
    {
      var ammoData = new AmmunitionData { Name = "AP Round", Description = "Armor-piercing ammo" };
      var ammo = new Ammunition(ammoData);
      Assert.Equal("AP Round", ammo.AmmoName);
      Assert.Equal("Armor-piercing ammo", ammo.AmmoDescription);
    });

    T("FirearmWeapon initializes AmmoType from DefaultAmmoData", () =>
    {
      var data = new FirearmWeaponData
      {
        DefaultAmmoData = new AmmunitionData { Name = "Standard", Description = "Standard issue rounds" },
        DamageStat = new Stat { StatType = StatType.Damage, BaseValue = 10 },
        CriticalChanceStat = new Stat { StatType = StatType.CriticalChance, BaseValue = 5 },
        RangeStat = new Stat { StatType = StatType.Range, BaseValue = 15 },
        AmmunitionStat = new Stat { StatType = StatType.Ammunition, BaseValue = 12 },
        ModSlotCount = 0,
      };
      var weapon = new FirearmWeapon(data);
      Assert.Equal("Standard", weapon.AmmoType.AmmoName);
      Assert.Equal("Standard issue rounds", weapon.AmmoType.AmmoDescription);
    });

    T("FirearmWeapon AmmoType can be reassigned", () =>
    {
      var data = MakeFirearmWeaponData();
      var weapon = new FirearmWeapon(data);
      var newAmmo = new Ammunition(new AmmunitionData { Name = "Incendiary" });
      weapon.AmmoType = newAmmo;
      Assert.Equal("Incendiary", weapon.AmmoType.AmmoName);
    });

    T("Ammunition struct inherits StatModifier array from AmmunitionData", () =>
    {
      var ammoData = new AmmunitionData
      {
        Name = "+Damage Ammo",
        Modifiers = [StatModifier.Add(10)]
      };
      var ammo = new Ammunition(ammoData);
      Assert.Equal(1, ammo.statModifiers.Count);
    });

    T("AmmunitionedWeaponData holds AmmunitionStat and DefaultAmmoData", () =>
    {
      var data = new AmmunitionedWeaponData
      {
        AmmunitionStat = new Stat { StatType = StatType.Ammunition, BaseValue = 6 },
        DefaultAmmoData = new AmmunitionData { Name = "Test Ammo" },
        DamageStat = new Stat { StatType = StatType.Damage, BaseValue = 20 },
        CriticalChanceStat = new Stat { StatType = StatType.CriticalChance, BaseValue = 10 },
        RangeStat = new Stat { StatType = StatType.Range, BaseValue = 5 },
      };
      Assert.Equal(6, data.AmmunitionStat.BaseValue);
      Assert.Equal("Test Ammo", data.DefaultAmmoData.Name);
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
