using FunProject.Combatants;
using FunProject.Geoscape;
using FunProject.Items;
using FunProject.Items.Capabilities;
using FunProject.Stats;
using FunProject.Weapons;
using Godot;
using GdUnit4;

namespace FunProject.Tests;

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

  [TestCase(TestName = "Campaign aggregation counts equipment once and stays fresh per gather")]
  public static void CampaignAggregationIsFreshAndCountsEquipmentOnce()
  {
    using var campaign = EquippedSoldierCampaign();
    var combatant = campaign.State.Roster[0];

    // Fresh materialization per call: repeated gathering never accumulates.
    Assert.Equal(75, Mathf.RoundToInt(combatant.Resolve<AimStat>(campaign.State.CampaignStatContributions(combatant))));
    Assert.Equal(75, Mathf.RoundToInt(combatant.Resolve<AimStat>(campaign.State.CampaignStatContributions(combatant))));

    var before = campaign.State.CampaignStatContributions(combatant); // gathered while equipped
    combatant.UnequipWeapon();

    Assert.Equal(75, Mathf.RoundToInt(combatant.Resolve<AimStat>(before)));
    Assert.Equal(70, Mathf.RoundToInt(combatant.Resolve<AimStat>(campaign.State.CampaignStatContributions(combatant))));
  }

  [TestCase(TestName = "StatsText resolves every stat against the supplied contribution set")]
  public static void StatsTextResolvesAllStatsAgainstTheSuppliedSet()
  {
    using var campaign = EquippedSoldierCampaign();
    var combatant = campaign.State.Roster[0];
    var contributions = campaign.State.CampaignStatContributions(combatant);
    string text = CombatantSummary.StatsText(combatant, "Health", contributions);

    Assert.True(text.Contains("Health: 20", System.StringComparison.Ordinal), text);
    Assert.True(text.Contains("Action Points: 4", System.StringComparison.Ordinal), text);
    Assert.True(text.Contains("Will: 50", System.StringComparison.Ordinal), text);
    Assert.True(text.Contains("Movement: 12", System.StringComparison.Ordinal), text);
    Assert.True(text.Contains("Vision: 20", System.StringComparison.Ordinal), text);
    Assert.True(text.Contains("Aim: 75", System.StringComparison.Ordinal), text); // own +10 and weapon +5 folded once each

    // Captivity and squad summaries differ only in the Health label.
    Assert.Equal(
      text.Replace("Health:", "Health (max):", System.StringComparison.Ordinal),
      CombatantSummary.StatsText(combatant, "Health (max)", contributions));
  }

  [TestCase(TestName = "EquipmentText lists weapon, armor, and utility slots; mods optional")]
  public static void EquipmentTextListsSlotsWithOptionalModRows()
  {
    using var campaign = EquippedSoldierCampaign();
    var combatant = campaign.State.Roster[0];
    combatant.EquipArmor(MakeArmor("Vest"));
    combatant.EquipItem(new EquippableItem(MakeUtilityData("Medkit")).With<UtilityCapability>().RequireSome(), 0);

    string squad = CombatantSummary.EquipmentText(combatant, includeMods: true);
    Assert.True(squad.Contains("Weapon: Rifle", System.StringComparison.Ordinal), squad);
    Assert.True(squad.Contains("Armor: Vest", System.StringComparison.Ordinal), squad);
    Assert.True(squad.Contains("Utility 1: Medkit", System.StringComparison.Ordinal), squad);
    Assert.True(squad.Contains("Personal mods: Scope", System.StringComparison.Ordinal), squad); // mod rows only when asked
    Assert.True(squad.Contains("Weapon mods: Rail", System.StringComparison.Ordinal), squad);

    string captivity = CombatantSummary.EquipmentText(combatant, includeMods: false);
    Assert.True(captivity.Contains("Weapon: Rifle", System.StringComparison.Ordinal), captivity);
    Assert.True(captivity.Contains("Armor: Vest", System.StringComparison.Ordinal), captivity);
    Assert.False(captivity.Contains("Scope", System.StringComparison.Ordinal), captivity);
    Assert.False(captivity.Contains("Rail", System.StringComparison.Ordinal), captivity);
  }

  [TestCase(TestName = "Unequipped slots have explicit empty text on every row")]
  public static void UnequippedSlotsHaveExplicitEmptyText()
  {
    using var campaign = new GeoscapeFixture(TestData.MakeStart(
      roster: [TestData.MakeEntry("Bare", TestData.MakeCombatantData("Bare"))]));
    var bare = campaign.State.Roster[0];

    string text = CombatantSummary.EquipmentText(bare, includeMods: true);
    Assert.True(text.Contains("Weapon: — empty —", System.StringComparison.Ordinal), text);
    Assert.True(text.Contains("Armor: — empty —", System.StringComparison.Ordinal), text);
    Assert.True(text.Contains("Utility 1: — empty —", System.StringComparison.Ordinal), text);
    Assert.True(text.Contains("Personal mods: — empty —", System.StringComparison.Ordinal), text);
    Assert.False(text.Contains("Weapon mods:", System.StringComparison.Ordinal), text); // no weapon: no weapon-mod row
  }
}
