using FunProject.GameState;
using FunProject.Items;
using FunProject.Stats;
using GdUnit4;

[TestSuite]
[RequireGodotRuntime]
public class GameStateTest
{
  [TestCase]
  public void CampaignCaptivityIsIsolatedFromOtherCampaigns()
  {
    var first = new GameState(MakeStart());
    var second = new GameState(MakeStart());
    var combatant = TestData.MakeCombatant("Hostile", TestData.MakeFaction("Enemy"));
    first.Captivity.Add(combatant);

    Assert.Equal(1, first.Captivity.Combatants.Count);
    Assert.Equal(0, second.Captivity.Combatants.Count);
  }

  [TestCase(TestName = "Roster is stamped: one Combatant per entry, override wins, empty falls back")]
  public void RosterIsStamped()
  {
    var state = new GameState(TestData.MakeStart(roster:
    [
      TestData.MakeEntry("Cpl. Ada Voss"),
      TestData.MakeEntry(""),            // fallback to the mold name
      TestData.MakeEntry("Sgt. Bram Okafor", unit: TestData.MakeCombatantData("Trooper", actionPoints: 8, movement: 14, vision: 22, aim: 60, modSlotCount: 2, will: 60)),
    ]));

    Assert.Equal(3, state.Roster.Count);
    Assert.Equal("Cpl. Ada Voss", state.Roster[0].Name);
    Assert.Equal("Mold", state.Roster[1].Name); // MakeEntry's default mold name
    Assert.Equal("Sgt. Bram Okafor", state.Roster[2].Name);
  }

  [TestCase(TestName = "Every roster combatant belongs to the player faction")]
  public void RosterBelongsToPlayerFaction()
  {
    var state = new GameState(TestData.MakeStart(roster: [TestData.MakeEntry("A"), TestData.MakeEntry("B")]));

    foreach (var unit in state.Roster)
      Assert.Equal(state.PlayerFaction, unit.OwningFaction);
  }

  [TestCase(TestName = "Map size and regions bind from the campaign map")]
  public void MapBinds()
  {
    var state = new GameState(TestData.MakeStart(regions: [TestData.MakeRegion("Northmark")]));

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
    Assert.Throws<System.InvalidOperationException>(() => new GameState(TestData.MakeStart(roster:
    [
      new RosterEntryData { Unit = null!, DisplayName = "Broken" },
    ])));
  }

  [TestCase(TestName = "Armory is built from CampaignStartData with flag-driven stock policy")]
  public void ArmoryBuiltFromStart()
  {
    EquippableItemData pistol = new() { Name = "Pistol" };
    var state = new GameState(TestData.MakeStart(armory: [pistol],
      modStock: [new MultiStatMod { Name = "Chip" }]));

    Assert.Equal(1, state.Armory.ItemStock().Count);
    Assert.Equal(1, state.Armory.ItemStock()[0].Remaining); // scarce entries seed one instance
    Assert.Equal(1, state.Armory.ModStock().Count);
    Assert.True(state.Armory.TryWithdrawItem(pistol).IsSome);
    Assert.False(state.Armory.TryWithdrawItem(pistol).IsSome);
  }

  [TestCase(TestName = "Bad armory authoring fails GameState construction")]
  public void BadArmoryFailsConstruction()
  {
    EquippableItemData dup = new() { Name = "Duplicate" };
    Assert.Throws<InvalidOperationException>(() =>
      new GameState(TestData.MakeStart(armory: [dup, dup])));
  }
}
