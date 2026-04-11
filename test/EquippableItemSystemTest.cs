using FunProject.Items;
using FunProject.Items.Effects;
using FunProject.Stats;
using FunProject.Weapons;
using Godot;

public partial class EquippableItemSystemTest : TestRunner
{
  public override void _Ready()
  {
    T("EquippableItem initializes charges and mod slots from data", () =>
    {
      var item = new EquippableItem(new EquippableItemData
      {
        Name = "Toolkit",
        Description = "General utility kit",
        MaxCharges = 3,
        ModSlotCount = 2,
      });

      Assert.Equal("Toolkit", item.ItemName);
      Assert.Equal("General utility kit", item.ItemDescription);
      Assert.Equal(3, item.MaxCharges);
      Assert.Equal(3, item.CurrentCharges);
      Assert.Equal(2, item.GetModSlots().Count);
    });

    T("EquippableItem can spend and restore charges", () =>
    {
      var item = new EquippableItem(new EquippableItemData
      {
        Name = "Charge Pack",
        MaxCharges = 2,
      });

      Assert.True(item.TrySpendCharge());
      Assert.Equal(1, item.CurrentCharges);
      Assert.True(item.TrySpendCharge());
      Assert.True(item.IsDepleted);
      Assert.False(item.TrySpendCharge());

      item.RestoreCharges();
      Assert.Equal(2, item.CurrentCharges);
    });

    T("Weapon inherits base equippable item behavior", () =>
    {
      var weapon = new MeleeWeapon(new WeaponData
      {
        Name = "Blade",
        Description = "Close combat weapon",
        DamageElement = DamageElement.Kinetic,
        DamageStat = new DamageStat { BaseValue = 7 },
        RangeStat = new RangeStat { BaseValue = 1 },
        CriticalChanceStat = new CriticalChanceStat { BaseValue = 5 },
        ModSlotCount = 1,
      });

      Assert.Equal("Blade", weapon.ItemName);
      Assert.Equal("Blade", weapon.WeaponName);
      Assert.Equal(1, weapon.GetModSlots().Count);
      Assert.Equal(7, weapon.GetDamageStat().BaseValue);
    });

    T("Grenade copies serializable data and effect descriptors", () =>
    {
      var damageEffect = new DamageEffectData
      {
        Name = "Shrapnel",
        BaseDamage = 6,
        DamageElement = DamageElement.Kinetic,
      };

      var grenade = new Grenade(new GrenadeData
      {
        Name = "Frag Grenade",
        Description = "Explodes into fragments",
        ThrowRange = 5,
        ActionPointCost = 2,
        BlastRadius = 3,
        MaxCharges = 1,
        Effects = [damageEffect],
      });

      Assert.Equal("Frag Grenade", grenade.ItemName);
      Assert.Equal(5, grenade.ThrowRange);
      Assert.Equal(2, grenade.ActionPointCost);
      Assert.Equal(3, grenade.BlastRadius);
      Assert.Equal(1, grenade.Effects.Count);
      Assert.Equal(damageEffect, grenade.Effects[0]);
    });

    Report();
  }
}
