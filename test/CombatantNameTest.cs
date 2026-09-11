using FunProject.Combatants;
using GdUnit4;

[TestSuite]
[RequireGodotRuntime]
public class CombatantNameTest
{
  [TestCase(TestName = "Name falls back to the mold's name when no override is given")]
  public void NameFallsBackToData()
  {
    var combatant = new Combatant(
      TestData.MakeCombatantData("Trooper", actionPoints: 8, movement: 14, vision: 22, aim: 60, modSlotCount: 2, will: 60),
      new Faction(new FactionData()));

    Assert.Equal("Trooper", combatant.Name);
  }

  [TestCase(TestName = "Name override wins over the mold's name")]
  public void NameOverrideWins()
  {
    var combatant = new Combatant(
      TestData.MakeCombatantData("Trooper", actionPoints: 8, movement: 14, vision: 22, aim: 60, modSlotCount: 2, will: 60),
      new Faction(new FactionData()), Some("Cpl. Ada Voss"));

    Assert.Equal("Cpl. Ada Voss", combatant.Name);
  }
}
