using FunProject.Core;
using FunProject.Items;
using FunProject.Items.Capabilities;
using FunProject.Stats;
using GdUnit4;
using System;

[TestSuite]
[RequireGodotRuntime]
public class ArmorCapabilityTest
{
  private static ArmorCapabilityData MakeData(
    int armor = 10,
    Element element = Element.Kinetic,
    int regenDelayTurns = 0,
    int regenPerTurn = 0) => new()
    {
      ArmorStat = new BaseArmorStat { BaseValue = armor, Element = element },
      RegenDelayTurns = regenDelayTurns,
      RegenPerTurn = regenPerTurn,
    };

  [TestCase(TestName = "Armor capability starts at max with the stat's element")]
  public void StartsAtMaxWithElement()
  {
    var capability = new ArmorCapability(MakeData(armor: 12, element: Element.Thermal));

    Assert.Equal(12, capability.Max);
    Assert.Equal(12, capability.Current);
    Assert.Equal(Element.Thermal, capability.Element);
    Assert.False(capability.CanRegen);
    Assert.Equal(0, capability.RegenDelayRemaining);
  }

  [TestCase(TestName = "Reduce clamps current armor at zero")]
  public void ReduceClampsAtZero()
  {
    var capability = new ArmorCapability(MakeData(armor: 5));

    capability.Reduce(3);
    Assert.Equal(2, capability.Current);
    capability.Reduce(10);
    Assert.Equal(0, capability.Current);
    Assert.True(capability.IsDepleted);
  }

  [TestCase(TestName = "Reduce rejects negative amounts")]
  public void ReduceRejectsNegativeAmounts()
  {
    var capability = new ArmorCapability(MakeData());
    Assert.Throws<ArgumentOutOfRangeException>(() => capability.Reduce(-1));
  }

  [TestCase(TestName = "TickRegen decrements the delay before restoring armor")]
  public void TickRegenDecrementsDelayBeforeRestoring()
  {
    var capability = new ArmorCapability(MakeData(armor: 10, regenDelayTurns: 2, regenPerTurn: 3));
    capability.Reduce(8);
    capability.RearmRegenDelay();
    Assert.Equal(2, capability.RegenDelayRemaining);

    Assert.Equal(0, capability.TickRegen());
    Assert.Equal(1, capability.RegenDelayRemaining);
    Assert.Equal(0, capability.TickRegen());
    Assert.Equal(0, capability.RegenDelayRemaining);
    Assert.Equal(2, capability.Current);

    Assert.Equal(3, capability.TickRegen());
    Assert.Equal(5, capability.Current);
  }

  [TestCase(TestName = "TickRegen caps at max armor")]
  public void TickRegenCapsAtMax()
  {
    var capability = new ArmorCapability(MakeData(armor: 10, regenDelayTurns: 0, regenPerTurn: 50));
    capability.Reduce(6);
    capability.RearmRegenDelay();

    Assert.Equal(6, capability.TickRegen());
    Assert.Equal(10, capability.Current);
    Assert.Equal(0, capability.TickRegen());
  }

  [TestCase(TestName = "Reduce does not re-arm the regen delay")]
  public void ReduceDoesNotReArmRegenDelay()
  {
    var capability = new ArmorCapability(MakeData(armor: 10, regenDelayTurns: 2, regenPerTurn: 3));

    capability.Reduce(4);
    Assert.Equal(0, capability.RegenDelayRemaining);
    Assert.Equal(3, capability.TickRegen());
    Assert.Equal(9, capability.Current);

    capability.RearmRegenDelay();
    Assert.Equal(0, capability.TickRegen());
    Assert.Equal(1, capability.RegenDelayRemaining);
    capability.Reduce(1);
    Assert.Equal(1, capability.RegenDelayRemaining);
  }

  [TestCase(TestName = "Negative authored values clamp to zero")]
  public void NegativeAuthoredValuesClampToZero()
  {
    var capability = new ArmorCapability(MakeData(armor: -5, regenDelayTurns: -1, regenPerTurn: -2));

    Assert.Equal(0, capability.Max);
    Assert.Equal(0, capability.Current);
    Assert.Equal(0, capability.RegenDelayTurns);
    Assert.Equal(0, capability.RegenPerTurn);
    Assert.False(capability.CanRegen);
  }

  [TestCase(TestName = "Armor capability without an armor stat throws")]
  public void MissingArmorStatThrows()
  {
    Assert.Throws<InvalidOperationException>(
      () => new ArmorCapability(new ArmorCapabilityData { ArmorStat = null! }));
  }

  [TestCase(TestName = "Item with armor capability data exposes the runtime capability")]
  public void ItemExposesRuntimeCapability()
  {
    var item = new EquippableItem(new EquippableItemData
    {
      Name = "Plate Vest",
      Description = "Test armor",
      Capabilities = [MakeData(armor: 7, element: Element.Chem)],
    });

    var capability = item.FindCapability<ArmorCapability>().RequireSome();
    Assert.Equal(7, capability.Max);
    Assert.Equal(Element.Chem, capability.Element);
    Assert.True(item.With<ArmorCapability>().IsSome);
  }
}
