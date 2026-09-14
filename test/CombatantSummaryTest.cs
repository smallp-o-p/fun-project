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
  // One campaign with one soldier carrying a personal Aim mod and a weapon with its own
  // Aim mod: the campaign contribution set is own + condition tiers + equipped weapon,
  // each counted exactly once.
  private static GeoscapeFixture EquippedSoldierCampaign()
  {
    var campaign = new GeoscapeFixture(TestData.MakeStart(
      roster: [TestData.MakeEntry("Alpha", TestData.MakeCombatantData(
        "Alpha", health: 20, aim: 60, modSlotCount: 1))]));
    var alpha = campaign.State.Roster[0];
    alpha.GetModSlots()[0].Equip(new MultiStatMod
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
    alpha.EquipWeapon(rifle);
    return campaign;
  }

  [TestCase(TestName = "Campaign aggregation folds own mods and the equipped weapon's once")]
  public void CampaignAggregationFoldsOwnAndWeaponContributionsOnce()
  {
    using var campaign = EquippedSoldierCampaign();
    var combatant = campaign.State.Roster[0];

    Assert.Equal(75, Mathf.RoundToInt(combatant.Resolve<AimStat>(campaign.State.CampaignStatContributions(combatant))));
    // Fresh materialization per call: repeated gathering never accumulates.
    Assert.Equal(75, Mathf.RoundToInt(combatant.Resolve<AimStat>(campaign.State.CampaignStatContributions(combatant))));
  }

  [TestCase(TestName = "Without a weapon only fresh gathering drops its contributions")]
  public void WithoutAWeaponOnlyFreshGatheringDropsItsContributions()
  {
    using var campaign = EquippedSoldierCampaign();
    var combatant = campaign.State.Roster[0];
    var before = campaign.State.CampaignStatContributions(combatant); // gathered while equipped
    combatant.UnequipWeapon();

    Assert.Equal(75, Mathf.RoundToInt(combatant.Resolve<AimStat>(before)));
    Assert.Equal(70, Mathf.RoundToInt(combatant.Resolve<AimStat>(campaign.State.CampaignStatContributions(combatant))));
  }

  [TestCase(TestName = "StatsText resolves every stat against the supplied contribution set")]
  public void StatsTextResolvesAllStatsAgainstTheSuppliedSet()
  {
    using var campaign = EquippedSoldierCampaign();
    var combatant = campaign.State.Roster[0];
    string text = CombatantSummary.StatsText(combatant, "Health",
      campaign.State.CampaignStatContributions(combatant));

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
    using var campaign = EquippedSoldierCampaign();
    var combatant = campaign.State.Roster[0];
    var contributions = campaign.State.CampaignStatContributions(combatant);

    Assert.Equal(
      CombatantSummary.StatsText(combatant, "Health", contributions).Replace("Health:", "Health (max):"),
      CombatantSummary.StatsText(combatant, "Health (max)", contributions));
  }

  [TestCase(TestName = "EquipmentText lists weapon, armor, and utility slots; mods optional")]
  public void EquipmentTextListsSlotsWithOptionalModRows()
  {
    using var campaign = EquippedSoldierCampaign();
    var combatant = campaign.State.Roster[0];
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
    using var campaign = new GeoscapeFixture(TestData.MakeStart(
      roster: [TestData.MakeEntry("Bare", TestData.MakeCombatantData("Bare"))]));
    var bare = campaign.State.Roster[0];

    string text = CombatantSummary.EquipmentText(bare, includeMods: true);
    Assert.True(text.Contains("Weapon: — empty —"), text);
    Assert.True(text.Contains("Armor: — empty —"), text);
    Assert.True(text.Contains("Utility 1: — empty —"), text);
    Assert.True(text.Contains("Personal mods: — empty —"), text);
    Assert.False(text.Contains("Weapon mods:"), text); // no weapon: no weapon-mod row
  }
}
