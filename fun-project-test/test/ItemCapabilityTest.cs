using FunProject.Items;
using FunProject.Items.Capabilities;
using FunProject.Items.Effects;
using FunProject.Tests;
using FunProject.Weapons;
using GdUnit4;
using System;

[TestSuite]
[RequireGodotRuntime]
public class ItemCapabilityTest
{
  [TestCase(TestName = "Charges capability initializes from data and spends down")]
  public void ChargesCapabilityInitializesFromDataAndSpendsDown()
  {
    var capability = (ChargesCapability)new ChargesCapabilityData { MaxCharges = 2 }.CreateRuntime();

    Assert.Equal(2, capability.MaxCharges);
    Assert.Equal(2, capability.Current);
    Assert.True(capability.TrySpend());
    Assert.Equal(1, capability.Current);
    Assert.True(capability.TrySpend());
    Assert.True(capability.IsDepleted);
    Assert.False(capability.TrySpend());

    capability.Restore();
    Assert.Equal(2, capability.Current);
  }

  [TestCase(TestName = "Negative max charges clamps to zero")]
  public void NegativeMaxChargesClampsToZero()
  {
    var capability = (ChargesCapability)new ChargesCapabilityData { MaxCharges = -3 }.CreateRuntime();

    Assert.Equal(0, capability.MaxCharges);
    Assert.True(capability.IsDepleted);
  }

  [TestCase(TestName = "Throwable capability copies authored values")]
  public void ThrowableCapabilityCopiesAuthoredValues()
  {
    var capability = (ThrowableCapability)new ThrowableCapabilityData
    {
      ThrowRange = 5,
      ActionPointCost = 2,
      ConsumesOnUse = false,
    }.CreateRuntime();

    Assert.Equal(5, capability.ThrowRange);
    Assert.Equal(2, capability.ActionPointCost);
    Assert.False(capability.ConsumesOnUse);
  }

  [TestCase(TestName = "Blast capability carries radius and effect descriptors")]
  public void BlastCapabilityCarriesRadiusAndEffectDescriptors()
  {
    var damageEffect = new DamageEffectData
    {
      Name = "Shrapnel",
      BaseDamage = 6,
      DamageElement = DamageElement.Kinetic,
    };
    var capability = (BlastCapability)new BlastCapabilityData
    {
      BlastRadius = 3,
      Effects = [damageEffect],
    }.CreateRuntime();

    Assert.Equal(3, capability.BlastRadius);
    Assert.Equal(1, capability.Effects.Count);
    Assert.Equal(damageEffect, capability.Effects[0]);
  }

  [TestCase(TestName = "Mod slots capability creates slot instances")]
  public void ModSlotsCapabilityCreatesSlotInstances()
  {
    var capability = (ModSlotsCapability)new ModSlotsCapabilityData { SlotCount = 2 }.CreateRuntime();

    Assert.Equal(2, capability.Slots.Count);
  }

  [TestCase(TestName = "FindCapability returns Some for attached and None for absent")]
  public void FindCapabilityReturnsSomeForAttachedAndNoneForAbsent()
  {
    var item = new EquippableItem(new EquippableItemData
    {
      Name = "Beacon",
      Capabilities = [new ThrowableCapabilityData { ThrowRange = 6 }],
    });

    Assert.Equal(6, item.FindCapability<ThrowableCapability>().RequireSome().ThrowRange);
    Assert.True(item.FindCapability<ChargesCapability>().IsNone);
  }

  [TestCase(TestName = "With returns a proof binding item and capability")]
  public void WithReturnsProofBindingItemAndCapability()
  {
    var item = new EquippableItem(new EquippableItemData
    {
      Name = "Beacon",
      Capabilities = [new ThrowableCapabilityData()],
    });

    ItemWith<ThrowableCapability> proof = item.With<ThrowableCapability>().RequireSome();
    Assert.True(ReferenceEquals(item, proof.Item));
    Assert.True(ReferenceEquals(item.FindCapability<ThrowableCapability>().RequireSome(), proof.Capability));
    Assert.True(item.With<ChargesCapability>().IsNone);
  }

  [TestCase(TestName = "Duplicate capability types throw at construction")]
  public void DuplicateCapabilityTypesThrowAtConstruction()
  {
    Assert.Throws<InvalidOperationException>(() => new EquippableItem(new EquippableItemData
    {
      Name = "Broken",
      Capabilities = [new ThrowableCapabilityData(), new ThrowableCapabilityData()],
    }));
  }
}
