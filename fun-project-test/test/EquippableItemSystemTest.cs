using FunProject.Core;
using FunProject.Items;
using FunProject.Items.Capabilities;
using FunProject.Items.Effects;
using FunProject.Weapons;
using GdUnit4;

[TestSuite]
[RequireGodotRuntime]
public class EquippableItemSystemTest
{
  [TestCase(TestName = "Bare item has identity and no capabilities")]
  public void BareItemHasIdentityAndNoCapabilities()
  {
    var item = new EquippableItem(new EquippableItemData
    {
      Name = "Toolkit",
      Description = "General utility kit",
    });

    Assert.Equal("Toolkit", item.ItemName);
    Assert.Equal("General utility kit", item.ItemDescription);
    Assert.True(item.FindCapability<ChargesCapability>().IsNone);
    Assert.True(item.With<ThrowableCapability>().IsNone);
    Assert.Equal(0, item.GetModSlots().Count);
  }

  [TestCase(TestName = "Mod slots capability exposes slots")]
  public void ModSlotsCapabilityExposesSlots()
  {
    var item = new EquippableItem(new EquippableItemData
    {
      Name = "Rig",
      Capabilities = [new ModSlotsCapabilityData { SlotCount = 2 }],
    });

    Assert.Equal(2, item.FindCapability<ModSlotsCapability>().RequireSome().Slots.Count);
    Assert.Equal(2, item.GetModSlots().Count);
  }

  [TestCase(TestName = "Frag grenade is pure data")]
  public void FragGrenadeIsPureData()
  {
    var damageEffect = new DamageEffectData
    {
      Name = "Shrapnel",
      BaseDamage = 6,
      Element = Element.Kinetic,
    };

    var grenade = new EquippableItem(new EquippableItemData
    {
      Name = "Frag Grenade",
      Description = "Explodes into fragments",
      Capabilities =
      [
        new ThrowableCapabilityData { ThrowRange = 5, ActionPointCost = 2 },
        new BlastCapabilityData { BlastRadius = 3, Effects = [damageEffect] },
        new ChargesCapabilityData { MaxCharges = 1 },
      ],
    });

    Assert.Equal("Frag Grenade", grenade.ItemName);
    var throwable = grenade.FindCapability<ThrowableCapability>().RequireSome();
    Assert.Equal(5, throwable.ThrowRange);
    Assert.Equal(2, throwable.ActionPointCost);
    var blast = grenade.FindCapability<BlastCapability>().RequireSome();
    Assert.Equal(3, blast.BlastRadius);
    Assert.Equal(1, blast.Effects.Count);
    Assert.Equal(damageEffect, blast.Effects[0]);
  }
}
