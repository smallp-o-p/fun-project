#nullable disable warnings
using GdUnit4;

namespace FunProject.Tests;

[TestSuite]
[RequireGodotRuntime]
public partial class TestDataTest
{
  [TestCase]
  public void DefaultResourceGraphsHaveIndependentRankTables()
  {
    var first = TestData.MakeCombatantData();
    var second = TestData.MakeCombatantData();
    Assert.False(ReferenceEquals(first.RankTable, second.RankTable));
    first.RankTable.Levels[0].Name = "Changed";
    Assert.Equal("Rookie", second.RankTable.Levels[0].Name);
    Assert.Equal(3, second.RankTable.Levels.Count);
  }

  [TestCase]
  public void ExplicitResourceIdentityAndNumericOverridesAreRetained()
  {
    var ranks = TestData.MakeRankTable();
    var data = TestData.MakeCombatantData(actionPoints: 8, movement: 14,
      vision: 22, aim: 60, modSlotCount: 2, will: 60, rankTable: ranks);
    Assert.True(ReferenceEquals(ranks, data.RankTable));
    Assert.True(data.ActionPointsStat.BaseValue == 8);
    Assert.True(data.WillStat.BaseValue == 60);
    Assert.True(data.MovementStat.BaseValue == 14);
    Assert.True(data.VisionStat.BaseValue == 22);
    Assert.True(data.AimStat.BaseValue == 60);
    Assert.Equal(2, data.ModSlotCount);
  }

  [TestCase]
  public void AuthoredLabelsKeepTheirOriginalDefaults()
  {
    var plain = TestData.MakeWeaponData();
    Assert.Equal("Name", plain.Name);
    Assert.Equal("Description", plain.Description);
    Assert.Equal("Rifle weapon", TestData.MakeAmmoWeaponData("Rifle").Description);
    Assert.Equal("Description", TestData.MakeAmmoWeaponData("Rifle", description: "Description").Description);
  }

  [TestCase]
  public void ArmoryResourceBuildersPreserveStockPolicy()
  {
    Assert.True(TestData.MakeItemData("Pistol", unlimited: true).UnlimitedStock);
    Assert.False(TestData.MakeItemData("Rifle").UnlimitedStock);
    Assert.True(TestData.MakeMod("Scope", unlimited: true).UnlimitedStock);
    Assert.False(TestData.MakeMod("Grip").UnlimitedStock);
  }
}
