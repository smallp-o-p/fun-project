#nullable disable warnings
using GdUnit4;

namespace FunProject.Tests;

[TestSuite]
[RequireGodotRuntime]
public partial class TestDataTest
{
  [TestCase]
  public static void DefaultResourceGraphsHaveIndependentRankTables()
  {
    var first = TestData.MakeCombatantData();
    var second = TestData.MakeCombatantData();
    Assert.False(ReferenceEquals(first.RankTable, second.RankTable));
    first.RankTable.Levels[0].Name = "Changed";
    Assert.Equal("Rookie", second.RankTable.Levels[0].Name);
    Assert.Equal(3, second.RankTable.Levels.Count);
  }
}
