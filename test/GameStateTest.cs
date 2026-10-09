using CampaignGameState = global::FunProject.GameState.GameState;
using System;
using FunProject.GameState;
using FunProject.Items;
using FunProject.Stats;
using GdUnit4;

namespace FunProject.Tests;

[TestSuite]
[RequireGodotRuntime]
public class GameStateTest
{
  [TestCase]
  public static void CampaignCaptivityIsIsolatedFromOtherCampaigns()
  {
    var first = new CampaignGameState(MakeStart());
    var second = new CampaignGameState(MakeStart());
    var combatant = TestData.MakeCombatant("Hostile", TestData.MakeFaction("Enemy"));
    first.Captivity.Add(combatant);

    Assert.Equal(1, first.Captivity.Combatants.Count);
    Assert.Equal(0, second.Captivity.Combatants.Count);
  }

  [TestCase(TestName = "Roster is stamped: one Combatant per entry, override wins, empty falls back, all owned by the player faction")]
  public static void RosterIsStamped()
  {
    var state = new CampaignGameState(TestData.MakeStart(roster:
    [
      TestData.MakeEntry("Cpl. Ada Voss"),
      TestData.MakeEntry(""),            // fallback to the mold name
      TestData.MakeEntry("Sgt. Bram Okafor", unit: TestData.MakeCombatantData("Trooper", actionPoints: 8, movement: 14, vision: 22, aim: 60, modSlotCount: 2, will: 60)),
    ]));

    Assert.Equal(3, state.Roster.Count);
    Assert.Equal("Cpl. Ada Voss", state.Roster[0].Name);
    Assert.Equal("Mold", state.Roster[1].Name); // MakeEntry's default mold name
    Assert.Equal("Sgt. Bram Okafor", state.Roster[2].Name);
    foreach (var unit in state.Roster)
      Assert.Equal(state.PlayerFaction, unit.OwningFaction);
  }

  [TestCase(TestName = "Map size and regions bind from the campaign map")]
  public static void MapBinds()
  {
    var state = new CampaignGameState(TestData.MakeStart(regions: [TestData.MakeRegion("Northmark")]));

    Assert.Equal(1, state.Regions.Count);
    Assert.Equal(0, state.IndexOfRegion("Northmark"));
    Assert.Equal(-1, state.IndexOfRegion("Nowhere"));
  }

  [TestCase(TestName = "Malformed campaign authoring fails GameState construction")]
  public static void MalformedCampaignAuthoringFailsConstruction()
  {
    EquippableItemData duplicate = new() { Name = "Duplicate" };
    (string Case, CampaignStartData Start)[] malformed =
    [
      ("missing player faction", new CampaignStartData
      {
        Map = new FunProject.Strategic.GeoscapeMapData(),
        PlayerFaction = null!,
      }),
      ("missing map", new CampaignStartData
      {
        Map = null!,
        PlayerFaction = new FunProject.Combatants.FactionData(),
      }),
      ("roster entry without a unit", TestData.MakeStart(roster:
      [
        new RosterEntryData { Unit = null!, DisplayName = "Broken" },
      ])),
      ("captive without a unit", TestData.MakeStart(captives:
      [
        new CaptiveEntryData { Unit = null!, Faction = new FunProject.Combatants.FactionData() },
      ])),
      ("captive without a faction", TestData.MakeStart(captives:
      [
        new CaptiveEntryData { Unit = TestData.MakeCombatantData("Grunt"), Faction = null! },
      ])),
      ("armory with a duplicated item reference", TestData.MakeStart(armory: [duplicate, duplicate])),
    ];
    foreach ((string name, CampaignStartData start) in malformed)
      Assert.Throws<InvalidOperationException>(() => _ = new CampaignGameState(start), name);
  }

  [TestCase(TestName = "Captives are stamped with override and fallback names and leave the roster untouched")]
  public static void CaptivesAreStamped()
  {
    var state = new CampaignGameState(TestData.MakeStart(roster: [TestData.MakeEntry("Sgt. Bram Okafor")], captives:
    [
      TestData.MakeCaptiveEntry("Captured Grunt 01"),
      TestData.MakeCaptiveEntry(),
    ]));

    var captives = state.Captivity.Combatants;
    Assert.Equal(2, captives.Count);
    Assert.True(captives.AsValueEnumerable().Any(c => string.Equals(c.Name, "Captured Grunt 01", StringComparison.Ordinal)));
    Assert.True(captives.AsValueEnumerable().Any(c => string.Equals(c.Name, "Grunt", StringComparison.Ordinal))); // MakeCaptiveEntry's default mold name
    Assert.Equal(1, state.Roster.Count); // captives never join the roster
  }

  [TestCase(TestName = "Captives keep their authored faction, separate from the player's and shared by seed reference")]
  public static void CaptiveFactionsAreSeparateAndShared()
  {
    var raiders = new FunProject.Combatants.FactionData { Name = "Alien Raiders" };
    var state = new CampaignGameState(TestData.MakeStart(captives:
    [
      TestData.MakeCaptiveEntry("One", faction: raiders),
      TestData.MakeCaptiveEntry("Two", faction: raiders),
      TestData.MakeCaptiveEntry("Three", faction: new FunProject.Combatants.FactionData { Name = "Cultists" }),
    ]));

    var captives = state.Captivity.Combatants;
    var one = captives.AsValueEnumerable().Single(c => string.Equals(c.Name, "One", StringComparison.Ordinal));
    var two = captives.AsValueEnumerable().Single(c => string.Equals(c.Name, "Two", StringComparison.Ordinal));
    var three = captives.AsValueEnumerable().Single(c => string.Equals(c.Name, "Three", StringComparison.Ordinal));
    Assert.Equal("Alien Raiders", one.OwningFaction.Name);
    Assert.False(ReferenceEquals(one.OwningFaction, state.PlayerFaction));
    Assert.True(ReferenceEquals(one.OwningFaction, two.OwningFaction));
    Assert.False(ReferenceEquals(one.OwningFaction, three.OwningFaction));
  }

  [TestCase(TestName = "Rebuilding the campaign mints fresh captive combatants and factions")]
  public static void CaptivesAreFreshAcrossCampaigns()
  {
    var start = TestData.MakeStart(captives: [TestData.MakeCaptiveEntry("One")]);
    var first = new CampaignGameState(start);
    var second = new CampaignGameState(start);

    var firstCaptive = first.Captivity.Combatants[0];
    var secondCaptive = second.Captivity.Combatants[0];
    Assert.False(ReferenceEquals(firstCaptive, secondCaptive));
    Assert.False(ReferenceEquals(firstCaptive.OwningFaction, secondCaptive.OwningFaction));
  }

  [TestCase(TestName = "Armory is built from CampaignStartData with flag-driven stock policy")]
  public static void ArmoryBuiltFromStart()
  {
    EquippableItemData pistol = new() { Name = "Pistol" };
    var state = new CampaignGameState(TestData.MakeStart(armory: [pistol],
      modStock: [new MultiStatMod { Name = "Chip" }]));

    Assert.Equal(1, state.Armory.ItemStock().Count);
    Assert.Equal(1, state.Armory.ItemStock()[0].Remaining); // scarce entries seed one instance
    Assert.Equal(1, state.Armory.ModStock().Count);
    Assert.True(state.Armory.TryWithdrawItem(pistol).IsSome);
    Assert.False(state.Armory.TryWithdrawItem(pistol).IsSome);
  }

}
