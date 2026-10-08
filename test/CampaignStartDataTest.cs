using FunProject.GameState;
using GdUnit4;

[TestSuite]
[RequireGodotRuntime]
public class CampaignStartDataTest
{
  [TestCase(TestName = "Roster entry display name defaults to empty (fallback marker)")]
  public void DisplayNameDefaultsToEmpty()
  {
    var entry = new RosterEntryData
    {
      Unit = TestData.MakeCombatantData("Mold", actionPoints: 8, movement: 14, vision: 22, aim: 60, modSlotCount: 2, will: 60),
    };

    Assert.Equal("", entry.DisplayName);
  }
}
