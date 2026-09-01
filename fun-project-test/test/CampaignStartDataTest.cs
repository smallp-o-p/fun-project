using FunProject.GameState;
using GdUnit4;
using static FunProject.Tests.GeoscapeTestFactory;

[TestSuite]
[RequireGodotRuntime]
public class CampaignStartDataTest
{
  [TestCase(TestName = "Campaign start holds map, faction, and roster entries")]
  public void HoldsAuthoredFields()
  {
    var map = new FunProject.Strategic.GeoscapeMapData();
    var faction = new FunProject.Combatants.FactionData { Name = "Command" };
    var entry = MakeEntry("Cpl. Ada Voss");

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
    var entry = new RosterEntryData { Unit = MakeCombatantData() };

    Assert.Equal("", entry.DisplayName);
  }
}
