using System;
using FunProject.Combatants;
using FunProject.GameState;
using GdUnit4;

[TestSuite]
[RequireGodotRuntime]
public class CaptivityTest
{
  [TestCase]
  public void CaptivityDeduplicatesByCombatantReference()
  {
    var captivity = new Captivity();
    var data = TestData.MakeCombatantData("Hostile");
    var faction = TestData.MakeFaction("Enemy");
    var first = new Combatant(data, faction);
    var second = new Combatant(data, faction);
    captivity.Add(first);
    captivity.Add(first);
    captivity.Add(second);

    Assert.Equal(2, captivity.Combatants.Count);
    Assert.True(captivity.Combatants.AsValueEnumerable().Any(c => ReferenceEquals(c, first)));
    Assert.True(captivity.Combatants.AsValueEnumerable().Any(c => ReferenceEquals(c, second)));
  }

  [TestCase]
  public void ReAddingADuplicateDoesNotChangeMembership()
  {
    var captivity = new Captivity();
    var faction = TestData.MakeFaction("Enemy");
    var first = TestData.MakeCombatant("First", faction);
    var second = TestData.MakeCombatant("Second", faction);
    captivity.Add(first);
    captivity.Add(second);

    captivity.Add(first); // duplicate re-add

    var combatants = captivity.Combatants;
    Assert.Equal(2, combatants.Count);
    Assert.True(combatants.AsValueEnumerable().Any(c => ReferenceEquals(c, first)));
    Assert.True(combatants.AsValueEnumerable().Any(c => ReferenceEquals(c, second)));
  }

  [TestCase]
  public void ReturnedSnapshotDoesNotChangeWhenCaptivityGrows()
  {
    var captivity = new Captivity();
    var combatant = TestData.MakeCombatant("Hostile", TestData.MakeFaction("Enemy"));
    captivity.Add(combatant);
    var snapshot = captivity.Combatants;
    captivity.Add(TestData.MakeCombatant("Second", combatant.OwningFaction));

    Assert.Equal(1, snapshot.Count);
    Assert.True(ReferenceEquals(combatant, snapshot[0]));
  }

  [TestCase]
  public void NullCombatantIsRejected()
  {
    var captivity = new Captivity();
    Assert.Throws<ArgumentNullException>(() => captivity.Add(null!));
  }
}
