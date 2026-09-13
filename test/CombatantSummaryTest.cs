using FunProject.Combatants;
using FunProject.Geoscape;
using FunProject.Items;
using FunProject.Stats;
using FunProject.Weapons;
using Godot;
using GdUnit4;

[TestSuite]
[RequireGodotRuntime]
public class CombatantSummaryTest
{
  // One soldier with a personal Aim mod and a weapon carrying its own Aim mod: the
  // campaign contribution set is own + equipped weapon, each counted exactly once.
  private static Combatant EquippedSoldier()
  {
    var combatant = MakeCombatant("Alpha", MakeFaction("Cult"), aim: 60, modSlotCount: 1);
    combatant.GetModSlots()[0].Equip(new MultiStatMod
    {
      Name = "Scope",
      StatMods = [new AimStatMod { Modifiers = [StatModifier.Add(10)] }],
    });
    var rifle = new FirearmWeapon(MakeFirearmWeaponData("Rifle", modSlots: 1));
    rifle.GetModSlots()[0].Equip(new MultiStatMod
    {
      Name = "Rail",
      StatMods = [new AimStatMod { Modifiers = [StatModifier.Add(5)] }],
    });
    combatant.EquipWeapon(rifle);
    return combatant;
  }

  [TestCase(TestName = "Campaign aggregation folds own mods and the equipped weapon's once")]
  public void CampaignAggregationFoldsOwnAndWeaponContributionsOnce()
  {
    var combatant = EquippedSoldier();

    Assert.Equal(75, Mathf.RoundToInt(combatant.Resolve<AimStat>(combatant.CampaignStatContributions())));
    // Fresh materialization per call: repeated gathering never accumulates.
    Assert.Equal(75, Mathf.RoundToInt(combatant.Resolve<AimStat>(combatant.CampaignStatContributions())));
  }

  [TestCase(TestName = "Without a weapon only fresh gathering drops its contributions")]
  public void WithoutAWeaponOnlyOwnContributionsRemain()
  {
    var combatant = EquippedSoldier();
    var before = combatant.CampaignStatContributions(); // snapshot gathered while equipped
    combatant.UnequipWeapon();

    Assert.Equal(75, Mathf.RoundToInt(combatant.Resolve<AimStat>(before)));
    Assert.Equal(70, Mathf.RoundToInt(combatant.Resolve<AimStat>(combatant.CampaignStatContributions())));
  }

  [TestCase(TestName = "StatsText resolves every stat against the single gathered set")]
  public void StatsTextResolvesAllStatsAgainstOneGatheredSet()
  {
    string text = CombatantSummary.StatsText(EquippedSoldier(), "Health");

    Assert.True(text.Contains("Health: 20"), text);
    Assert.True(text.Contains("Action Points: 4"), text);
    Assert.True(text.Contains("Will: 50"), text);
    Assert.True(text.Contains("Movement: 12"), text);
    Assert.True(text.Contains("Vision: 20"), text);
    Assert.True(text.Contains("Aim: 75"), text); // own +10 and weapon +5 folded once each
  }

  [TestCase(TestName = "Captivity and squad summaries differ only in the Health label")]
  public void CaptivityAndSquadSummariesDifferOnlyInTheHealthLabel()
  {
    var combatant = EquippedSoldier();

    Assert.Equal(
      CombatantSummary.StatsText(combatant, "Health").Replace("Health:", "Health (max):"),
      CombatantSummary.StatsText(combatant, "Health (max)"));
  }

  [TestCase(TestName = "EquipmentText lists weapon, armor, and utility slots; mods optional")]
  public void EquipmentTextListsSlotsWithOptionalModRows()
  {
    var combatant = EquippedSoldier();
    combatant.EquipArmor(MakeArmor("Vest"));
    combatant.EquipItem(new EquippableItem(MakeItemData("Medkit")), 0);

    string squad = CombatantSummary.EquipmentText(combatant, includeMods: true);
    Assert.True(squad.Contains("Weapon: Rifle"), squad);
    Assert.True(squad.Contains("Armor: Vest"), squad);
    Assert.True(squad.Contains("Utility 1: Medkit"), squad);
    Assert.True(squad.Contains("Personal mods: Scope"), squad); // mod rows only when asked
    Assert.True(squad.Contains("Weapon mods: Rail"), squad);

    string captivity = CombatantSummary.EquipmentText(combatant, includeMods: false);
    Assert.True(captivity.Contains("Weapon: Rifle"), captivity);
    Assert.True(captivity.Contains("Armor: Vest"), captivity);
    Assert.False(captivity.Contains("Scope"), captivity);
    Assert.False(captivity.Contains("Rail"), captivity);
  }

  [TestCase(TestName = "Unequipped slots have explicit empty text on every row")]
  public void UnequippedSlotsHaveExplicitEmptyText()
  {
    var bare = MakeCombatant("Bare", MakeFaction("Cult"));

    string text = CombatantSummary.EquipmentText(bare, includeMods: true);
    Assert.True(text.Contains("Weapon: — empty —"), text);
    Assert.True(text.Contains("Armor: — empty —"), text);
    Assert.True(text.Contains("Utility 1: — empty —"), text);
    Assert.True(text.Contains("Personal mods: — empty —"), text);
    Assert.False(text.Contains("Weapon mods:"), text); // no weapon: no weapon-mod row
  }
}
