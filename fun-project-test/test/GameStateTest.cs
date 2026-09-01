using FunProject.GameState;
using GdUnit4;
using static FunProject.Tests.GeoscapeTestFactory;

[TestSuite]
[RequireGodotRuntime]
public class GameStateTest
{
  [TestCase(TestName = "Roster is stamped: one Combatant per entry, override wins, empty falls back")]
  public void RosterIsStamped()
  {
    var state = new GameState(MakeStart(roster:
    [
      MakeEntry("Cpl. Ada Voss"),
      MakeEntry(""),            // fallback to the mold name
      MakeEntry("Sgt. Bram Okafor", unit: MakeCombatantData("Trooper")),
    ]));

    Assert.Equal(3, state.Roster.Count);
    Assert.Equal("Cpl. Ada Voss", state.Roster[0].Name);
    Assert.Equal("Mold", state.Roster[1].Name); // MakeEntry's default mold name
    Assert.Equal("Sgt. Bram Okafor", state.Roster[2].Name);
  }

  [TestCase(TestName = "Every roster combatant belongs to the player faction")]
  public void RosterBelongsToPlayerFaction()
  {
    var state = new GameState(MakeStart(roster: [MakeEntry("A"), MakeEntry("B")]));

    foreach (var unit in state.Roster)
      Assert.Equal(state.PlayerFaction, unit.OwningFaction);
  }

  [TestCase(TestName = "Map size and regions bind from the campaign map")]
  public void MapBinds()
  {
    var state = new GameState(MakeStart(regions: [MakeRegion("Northmark")]));

    Assert.Equal(1, state.Regions.Count);
    Assert.Equal(0, state.IndexOfRegion("Northmark"));
    Assert.Equal(-1, state.IndexOfRegion("Nowhere"));
  }

  [TestCase(TestName = "Missing map or player faction throws at construction")]
  public void RequiredFieldsThrow()
  {
    Assert.Throws<System.InvalidOperationException>(() => new GameState(
      new CampaignStartData { Map = new FunProject.Strategic.GeoscapeMapData(), PlayerFaction = null! }));

    Assert.Throws<System.InvalidOperationException>(() => new GameState(
      new CampaignStartData { Map = null!, PlayerFaction = new FunProject.Combatants.FactionData() }));
  }

  [TestCase(TestName = "Roster entry without a unit throws at construction")]
  public void EntryWithoutUnitThrows()
  {
    Assert.Throws<System.InvalidOperationException>(() => new GameState(MakeStart(roster:
    [
      new RosterEntryData { Unit = null!, DisplayName = "Broken" },
    ])));
  }
}
