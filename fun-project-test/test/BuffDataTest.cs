using FunProject.Buffs;
using FunProject.Items;
using FunProject.Items.Capabilities;
using GdUnit4;

[TestSuite]
[RequireGodotRuntime]
public partial class BuffDataTest
{
  [TestCase(TestName = "BuffGrantCapabilityData creates a runtime capability exposing its buffs")]
  public void BuffGrantCapabilityExposesBuffs()
  {
    var buff = TestData.MakeBuff("Frenzy", new HealthBelowPercentCondition { Percent = 50f });
    var item = new EquippableItem(new EquippableItemData
    {
      Name = "Charm",
      Description = "Buff-granting trinket",
      Capabilities = [new BuffGrantCapabilityData { Buffs = [buff] }],
    });

    var capability = item.FindCapability<BuffGrantCapability>().RequireSome();
    Assert.Equal(1, capability.Buffs.Count);
    Assert.Equal(buff, capability.Buffs[0]);
  }

  [TestCase(TestName = "Combatant copies InnateBuffs from its data")]
  public void CombatantExposesInnateBuffs()
  {
    var faction = TestData.MakeFaction("Player");
    var buff = TestData.MakeBuff("Frenzy", new HealthBelowPercentCondition { Percent = 50f });
    var combatant = TestData.MakeCombatant("Alpha", faction, buffs: [buff]);

    Assert.Equal(1, combatant.InnateBuffs.Count);
    Assert.Equal(buff, combatant.InnateBuffs[0]);
  }
}
