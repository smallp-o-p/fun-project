using System;
using FunProject.GameState;
using FunProject.Staffing;
using FunProject.Strategic;
using GdUnit4;
using Godot;

namespace FunProject.Tests;

[TestSuite]
[RequireGodotRuntime]
public class GlobalBuffTest
{
  [TestCase]
  public static void AuthoredCampaignRetainsSharedBuffDefinitions()
  {
    string prefix = $"user://global-buff-{Guid.NewGuid():N}";
    string buffPath = prefix + "-buff.tres";
    string campaignPath = prefix + "-campaign.tres";
    using var authored = new GlobalBuffData
    { Name = "Research grant", Description = "Research is 50% faster.", Process = GlobalProcess.ResearchSpeed, Modifier = 0.5 };
    try
    {
      Assert.Equal(Error.Ok, ResourceSaver.Save(authored, buffPath));
      using var buff = ResourceLoader.Load<GlobalBuffData>(buffPath);
      using var start = MakeStart(researchProjects: [MakeResearch()]);
      start.InitialGlobalBuffs = [buff, buff];
      Assert.Equal(Error.Ok, ResourceSaver.Save(start, campaignPath));
      using var loaded = ResourceLoader.Load<CampaignStartData>(campaignPath, cacheMode: ResourceLoader.CacheMode.Ignore);
      Assert.True(ReferenceEquals(buff, loaded.InitialGlobalBuffs[0]));
      Assert.Equal("Research grant", buff.Name);
      Assert.Equal("Research is 50% faster.", buff.Description);
      using var campaign = new GeoscapeFixture(loaded);
      Assert.Equal(960L, campaign.Session.StartResearch(loaded.ResearchProjects[0]).RequireRight().CompletesAtTick);
      Assert.True(campaign.State.RemoveGlobalBuff(buff));
      Assert.Equal(1.0, campaign.State.ResearchSpeed);
    }
    finally
    {
      foreach (string path in new[] { campaignPath, buffPath })
        DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(path));
    }
  }

  [TestCase(StaffRole.Researcher)]
  [TestCase(StaffRole.Engineer)]
  public static void MembershipStacksPricesOnceAndIsOwnedByEachCampaign(StaffRole role)
  {
    var currency = MakeCurrency();
    var start = MakeStart(staffingRules: new StaffingRulesData
    {
      ResearcherCosts = new() { [currency] = 5 },
      EngineerCosts = new() { [currency] = 5 },
    }, startingCurrencies: new() { [currency] = 100 });
    var discount = new GlobalBuffData { Process = GlobalProcess.HiringCost, Modifier = -0.2 };
    var surcharge = new GlobalBuffData { Process = GlobalProcess.HiringCost, Modifier = 0.1 };
    start.InitialGlobalBuffs = [discount, discount];
    using var first = new GeoscapeFixture(start);
    using var second = new GeoscapeFixture(start);
    Assert.False(first.State.AddGlobalBuff(discount));
    Assert.True(first.State.AddGlobalBuff(surcharge));
    Assert.Equal(14UL, first.Session.GetHiringOption(role, 3).Costs[currency]);
    Assert.Equal(12UL, second.Session.GetHiringOption(role, 3).Costs[currency]);
    (role == StaffRole.Researcher ? first.Session.HireResearchers(3) : first.Session.HireEngineers(3)).RequireRight();
    Assert.Equal(86UL, first.State.GetCurrencyBalance(currency));
    Assert.True(first.State.RemoveGlobalBuff(discount));
    Assert.False(first.State.RemoveGlobalBuff(discount));
    Assert.Equal(6UL, new GeoscapeSession(first.State).GetHiringOption(role, 1).Costs[currency]);
    Assert.Equal(-0.2, discount.Modifier);
  }

  [TestCase(false)]
  [TestCase(true)]
  public static void StartedWorkKeepsItsBuffWhileLaterHiresChangeOnlyStaffSpeed(bool engineer)
  {
    var research = MakeResearch();
    var replacement = MakeResearch("Replacement");
    var recipe = MakeManufacturingRecipe(MakeItemData());
    using var campaign = new GeoscapeFixture(MakeStart(researchProjects: [research, replacement], manufacturingRecipes: [recipe],
      staffingRules: new StaffingRulesData { ResearcherSpeedBonus = 0.1f, EngineerSpeedBonus = 0.1f }));
    var buff = new GlobalBuffData { Process = engineer ? GlobalProcess.ManufacturingSpeed : GlobalProcess.ResearchSpeed, Modifier = 0.5 };
    campaign.State.AddGlobalBuff(buff);
    campaign.Session.StartResearch(research).RequireRight();
    campaign.Session.StartManufacturing(recipe).RequireRight();
    Assert.Equal(engineer ? 1440L : 960L, campaign.Session.ActiveResearch.RequireSome().CompletesAtTick);
    Assert.Equal(engineer ? 960L : 1440L, campaign.Session.ActiveManufacturing.RequireSome().CompletesAtTick);
    campaign.AdvanceTicks(480);
    campaign.State.RemoveGlobalBuff(buff);
    campaign.State.AddGlobalBuff(new GlobalBuffData { Process = buff.Process, Modifier = -0.5 });
    (engineer ? campaign.Session.HireEngineers(5) : campaign.Session.HireResearchers(5)).RequireRight();
    Assert.Equal(engineer ? 1440L : 840L, campaign.Session.ActiveResearch.RequireSome().CompletesAtTick);
    Assert.Equal(engineer ? 840L : 1440L, campaign.Session.ActiveManufacturing.RequireSome().CompletesAtTick);
    campaign.AdvanceTicks(360);
    if (engineer)
    {
      Assert.True(campaign.State.Armory.HasAvailableItem(recipe.Item));
      campaign.Session.StartManufacturing(recipe).RequireRight();
      Assert.Equal(2280L, campaign.Session.ActiveManufacturing.RequireSome().CompletesAtTick);
    }
    else
    {
      Assert.True(campaign.State.Research.IsCompleted(research));
      campaign.Session.StartResearch(replacement).RequireRight();
      Assert.Equal(2280L, campaign.Session.ActiveResearch.RequireSome().CompletesAtTick);
    }
  }

  [TestCase]
  public static void ExtremeModifiersKeepUnsignedPricesAndPositiveWorkValid()
  {
    var currency = MakeCurrency();
    var project = MakeResearch();
    using var campaign = new GeoscapeFixture(MakeStart(researchProjects: [project],
      staffingRules: new StaffingRulesData { ResearcherCosts = new() { [currency] = ulong.MaxValue } },
      startingCurrencies: new() { [currency] = ulong.MaxValue }));
    var expensive = new GlobalBuffData { Process = GlobalProcess.HiringCost, Modifier = double.MaxValue };
    campaign.State.AddGlobalBuff(expensive);
    Assert.Equal(ulong.MaxValue, campaign.Session.GetHiringOption(StaffRole.Researcher, uint.MaxValue).Costs[currency]);
    var opposite = new GlobalBuffData { Process = GlobalProcess.HiringCost, Modifier = -double.MaxValue };
    var another = new GlobalBuffData { Process = GlobalProcess.HiringCost, Modifier = double.MaxValue };
    campaign.State.AddGlobalBuff(opposite);
    campaign.State.AddGlobalBuff(another);
    campaign.State.RemoveGlobalBuff(opposite);
    Assert.True(campaign.State.AddGlobalBuff(opposite));
    campaign.State.RemoveGlobalBuff(opposite);
    campaign.State.RemoveGlobalBuff(another);
    campaign.State.RemoveGlobalBuff(expensive);
    var discount = new GlobalBuffData { Process = GlobalProcess.HiringCost, Modifier = -0.75 };
    campaign.State.AddGlobalBuff(discount);
    Assert.Equal(9223372036854775808UL, campaign.Session.GetHiringOption(StaffRole.Researcher, 2).Costs[currency]);
    Assert.Equal(ulong.MaxValue, campaign.Session.GetHiringOption(StaffRole.Researcher, 5).Costs[currency]);
    campaign.State.RemoveGlobalBuff(discount);
    campaign.State.AddGlobalBuff(new GlobalBuffData { Process = GlobalProcess.HiringCost, Modifier = -2 });
    Assert.Equal(0UL, campaign.Session.GetHiringOption(StaffRole.Researcher, 1).Costs[currency]);
    var slow = new GlobalBuffData { Process = GlobalProcess.ResearchSpeed, Modifier = -2 };
    campaign.State.AddGlobalBuff(slow);
    Assert.Equal(144000L, campaign.Session.StartResearch(project).RequireRight().CompletesAtTick);
    campaign.State.RemoveGlobalBuff(slow);
    var other = MakeResearch();
    // Use another campaign to exercise a fresh positive job without replacing the active project.
    var start = MakeStart(researchProjects: [other]);
    start.InitialGlobalBuffs = [new GlobalBuffData { Process = GlobalProcess.ResearchSpeed, Modifier = double.MaxValue }];
    using var fast = new GeoscapeFixture(start);
    Assert.Equal(1L, fast.Session.StartResearch(other).RequireRight().CompletesAtTick);
    fast.Session.HireResearchers(1).RequireRight();
    Assert.Equal(1L, fast.Session.ActiveResearch.RequireSome().CompletesAtTick);
    fast.AdvanceTicks(1);
    Assert.True(fast.State.Research.IsCompleted(other));
    Assert.Throws<InvalidOperationException>(() => campaign.State.AddGlobalBuff(
      new GlobalBuffData { Modifier = double.NaN }));
    Assert.Throws<InvalidOperationException>(() => campaign.State.AddGlobalBuff(
      new GlobalBuffData { Modifier = double.PositiveInfinity }));
    Assert.Throws<InvalidOperationException>(() => campaign.State.AddGlobalBuff(
      new GlobalBuffData { Process = (GlobalProcess)99 }));
  }
}
