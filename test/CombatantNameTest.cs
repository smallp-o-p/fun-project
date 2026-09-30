using FunProject.Combatants;
using GdUnit4;

[TestSuite]
[RequireGodotRuntime]
public class CombatantNameTest
{
  [TestCase(false, "Trooper", TestName = "Name falls back to the mold's name when no override is given")]
  [TestCase(true, "Cpl. Ada Voss", TestName = "Name override wins over the mold's name")]
  public void CombatantNameUsesOptionalOverride(bool overrideName, string expected)
  {
    var combatant = new Combatant(
      TestData.MakeCombatantData("Trooper", actionPoints: 8, movement: 14, vision: 22, aim: 60, modSlotCount: 2, will: 60),
      new Faction(new FactionData()),
      overrideName ? Some("Cpl. Ada Voss") : None);

    Assert.Equal(expected, combatant.Name);
  }
}
