using CampaignGameState = global::FunProject.GameState.GameState;
using FunProject.Combatants;
using FunProject.GameState;
using FunProject.Items;
using FunProject.Stats;
using GdUnit4;
using Godot;

namespace FunProject.Tests;

[TestSuite]
[RequireGodotRuntime]
public class DebugCampaignTest
{
  [TestCase]
  public static void AuthoredResearchChainUnlocksPrototypeThenSupply()
  {
    var start = GD.Load<CampaignStartData>("res://resources/geoscape/TestCampaign.tres");
    var sample = GD.Load<EquippableItemData>("res://resources/items/workshop_sample.tres");
    var prototypeItem = GD.Load<EquippableItemData>("res://resources/items/prototype_kit.tres");
    var supplyItem = GD.Load<EquippableItemData>("res://resources/items/supply_kit.tres");
    using var campaign = new GeoscapeFixture(start);

    Assert.Equal(2, start.ResearchProjects.Length);
    var prototype = start.ResearchProjects[0];
    var supply = start.ResearchProjects[1];
    Assert.Equal(1U, prototype.DurationDays);
    Assert.Equal(2U, supply.DurationDays);
    Assert.Equal(sample, campaign.Session.GetManufacturingOptions().AsValueEnumerable().Single().Project.Item);
    Assert.Equal(prototype, campaign.Session.GetAvailableResearchProjects().AsValueEnumerable().Single());
    campaign.Session.StartResearch(prototype).RequireRight();
    campaign.AdvanceTicks(1440);
    Assert.Equal(prototypeItem, campaign.Session.GetManufacturingOptions()[1].Project.Item);
    Assert.Equal(supply, campaign.Session.GetAvailableResearchProjects().AsValueEnumerable().Single());
    campaign.Session.StartResearch(supply).RequireRight();
    campaign.AdvanceTicks(2880);
    Assert.Equal(supplyItem, campaign.Session.GetManufacturingOptions()[2].Project.Item);
    Assert.Equal(0, campaign.Session.GetAvailableResearchProjects().Count);
  }

  // Content regression guard: pins the authored debug captives — three Alien Raiders
  // grunts beside the untouched four-member roster — against TestCampaign.tres.
  [TestCase]
  public static void DebugCampaignSeedsThreePersistentCaptives()
  {
    var start = GD.Load<CampaignStartData>("res://resources/geoscape/TestCampaign.tres");
    var state = new CampaignGameState(start);

    var captives = state.Captivity.Combatants;
    Assert.Equal(3, captives.Count);
    Assert.True(captives.AsValueEnumerable().Any(c => string.Equals(c.Name, "Captured Grunt 01", System.StringComparison.Ordinal)));
    Assert.True(captives.AsValueEnumerable().Any(c => string.Equals(c.Name, "Captured Grunt 02", System.StringComparison.Ordinal)));
    Assert.True(captives.AsValueEnumerable().Any(c => string.Equals(c.Name, "Captured Grunt 03", System.StringComparison.Ordinal)));

    // The player roster is untouched by the captive seed.
    Assert.Equal(4, state.Roster.Count);

    // Captives share one enemy runtime faction, separate from the player faction.
    Faction enemyFaction = captives[0].OwningFaction;
    Assert.Equal("Alien Raiders", enemyFaction.Name);
    Assert.False(ReferenceEquals(enemyFaction, state.PlayerFaction));
    Assert.True(ReferenceEquals(enemyFaction, captives[1].OwningFaction));
    Assert.True(ReferenceEquals(enemyFaction, captives[2].OwningFaction));

    // Every captive is stamped from the shared grunt template (stats keep the authored
    // grunt.tres stat instances).
    var grunt = GD.Load<CombatantData>("res://resources/combatants/grunt.tres");
    foreach (Combatant captive in captives)
      Assert.True(ReferenceEquals(grunt.HealthStat, captive.GetStat<HealthStat>()));
  }
}
