using FunProject.GameState;
using GdUnit4;

[TestSuite]
[RequireGodotRuntime]
public class CampaignStartDataTest
{
  [TestCase(TestName = "Campaign start holds map, faction, and roster entries")]
  public void HoldsAuthoredFields()
  {
    var map = new FunProject.Strategic.GeoscapeMapData();
    var faction = new FunProject.Combatants.FactionData { Name = "Command" };
    var entry = TestData.MakeEntry("Cpl. Ada Voss");

    var start = new CampaignStartData
    {
      Name = "Test Campaign",
      Map = map,
      PlayerFaction = faction,
      StartingRoster = [entry],
    };

    Assert.Equal(map, start.Map);
    Assert.Equal(faction, start.PlayerFaction);
    Assert.Equal(1, start.StartingRoster.Length);
    Assert.Equal("Cpl. Ada Voss", start.StartingRoster[0].DisplayName);
  }

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
