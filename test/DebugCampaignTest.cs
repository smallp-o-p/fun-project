using FunProject.Combatants;
using FunProject.GameState;
using FunProject.Stats;
using GdUnit4;
using Godot;

[TestSuite]
[RequireGodotRuntime]
public class DebugCampaignTest
{
  // Content regression guard: pins the authored debug captives — three Alien Raiders
  // grunts beside the untouched four-member roster — against TestCampaign.tres.
  [TestCase]
  public void DebugCampaignSeedsThreePersistentCaptives()
  {
    var start = GD.Load<CampaignStartData>("res://resources/geoscape/TestCampaign.tres");
    var state = new GameState(start);

    var captives = state.Captivity.Combatants;
    Assert.Equal(3, captives.Count);
    Assert.True(captives.AsValueEnumerable().Any(c => c.Name == "Captured Grunt 01"));
    Assert.True(captives.AsValueEnumerable().Any(c => c.Name == "Captured Grunt 02"));
    Assert.True(captives.AsValueEnumerable().Any(c => c.Name == "Captured Grunt 03"));

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
