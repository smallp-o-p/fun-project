using FunProject.Combatants;
using GdUnit4;
using static FunProject.Tests.GeoscapeTestFactory;

[TestSuite]
[RequireGodotRuntime]
public class CombatantNameTest
{
  [TestCase(TestName = "Name falls back to the mold's name when no override is given")]
  public void NameFallsBackToData()
  {
    var combatant = new Combatant(MakeCombatantData("Trooper"), new Faction(new FactionData()));

    Assert.Equal("Trooper", combatant.Name);
  }

  [TestCase(TestName = "Name override wins over the mold's name")]
  public void NameOverrideWins()
  {
    var combatant = new Combatant(
      MakeCombatantData("Trooper"), new Faction(new FactionData()), Some("Cpl. Ada Voss"));

    Assert.Equal("Cpl. Ada Voss", combatant.Name);
  }
}
