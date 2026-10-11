using System;
using FunProject.Engineering;
using FunProject.Staffing;
using FunProject.Strategic;
using GdUnit4;

namespace FunProject.Tests;

[TestSuite]
[RequireGodotRuntime]
public class StaffHiringTest
{
  [TestCase(StaffRole.Researcher)]
  [TestCase(StaffRole.Engineer)]
  public static void BatchPriceAndBonusAreCapturedAndCommitBeforeNotification(StaffRole role)
  {
    var currency = MakeCurrency();
    var other = MakeCurrency();
    var rules = new StaffingRulesData
    {
      ResearcherBonusUnits = 2000,
      EngineerBonusUnits = 3000,
      ResearcherCosts = new() { [currency] = 7, [other] = 2 },
      EngineerCosts = new() { [currency] = 11, [other] = 3 },
    };
    using var campaign = new GeoscapeFixture(MakeStart(staffingRules: rules,
      startingCurrencies: new() { [currency] = 100, [other] = 100 }));
    rules.ResearcherCosts[currency] = 99;
    rules.EngineerCosts.Clear();
    rules.ResearcherBonusUnits = rules.EngineerBonusUnits = 0;
    var preview = campaign.Session.GetHiringOption(role, 3).RequireRight();
    ulong price = role == StaffRole.Researcher ? 21UL : 33UL;
    ulong otherPrice = role == StaffRole.Researcher ? 6UL : 9UL;
    Assert.Equal(price, preview.Costs[currency]);
    Assert.True(preview.Affordable);
    // A presentation snapshot must not become the price charged by the backend.
    preview.Costs.Clear();
    campaign.Session.EventCommitted += e =>
    {
      if (e is not StaffHired hired) return;
      Assert.Equal(3, hired.Count);
      Assert.Equal(100UL - price, campaign.State.GetCurrencyBalance(currency));
      Assert.Equal(100UL - otherPrice, campaign.State.GetCurrencyBalance(other));
      Assert.Equal(3, Count(campaign, role));
      Assert.Equal(role == StaffRole.Researcher ? 16000L : 19000L, hired.RateUnits);
    };
    Hire(campaign, role, 3).RequireRight();
    Assert.Equal(0, Count(campaign, role == StaffRole.Researcher ? StaffRole.Engineer : StaffRole.Researcher));
    Assert.Equal(1, campaign.Events.EventsOf<StaffHired>().Length);
    var rebuilt = new GeoscapeSession(campaign.State);
    Assert.Equal(3, rebuilt.GetHiringOption(role, 1).RequireRight().Count);
  }

  [TestCase(StaffRole.Researcher)]
  [TestCase(StaffRole.Engineer)]
  public static void ShortfallInAnyCurrencyPreservesWorkEvenInsideTimeAdvanced(StaffRole role)
  {
    var funded = MakeCurrency("Same name");
    var missing = MakeCurrency("Same name");
    var rules = new StaffingRulesData
    {
      ResearcherCosts = new() { [funded] = 5, [missing] = 1 },
      EngineerCosts = new() { [funded] = 5, [missing] = 1 },
    };
    var recipe = MakeManufacturingRecipe(MakeItemData());
    var project = MakeResearch();
    using var campaign = new GeoscapeFixture(MakeStart(staffingRules: rules,
      startingCurrencies: new() { [funded] = 10 }, manufacturingRecipes: [recipe], researchProjects: [project]));
    var manufacturing = campaign.Session.StartManufacturing(recipe).RequireRight();
    var research = campaign.Session.StartResearch(project).RequireRight();
    Assert.False(campaign.Session.GetHiringOption(role, 2).RequireRight().Affordable);
    campaign.Session.EventCommitted += e =>
    {
      if (e is not TimeAdvanced) return;
      Assert.Equal(StaffHireFailure.InsufficientCurrencies, Hire(campaign, role, 2).RequireLeft());
      Assert.Equal(manufacturing, campaign.Session.ActiveManufacturing.RequireSome());
      Assert.Equal(research, campaign.Session.ActiveResearch.RequireSome());
      Assert.Equal(10UL, campaign.State.GetCurrencyBalance(funded));
      Assert.Equal(0UL, campaign.State.GetCurrencyBalance(missing));
      Assert.Equal(0, Count(campaign, role));
    };
    campaign.AdvanceTicks(1);
    Assert.Equal(0, campaign.Events.EventsOf<StaffHired>().Length);
  }

  [TestCase(0, StaffHireFailure.InvalidQuantity)]
  [TestCase(-1, StaffHireFailure.InvalidQuantity)]
  [TestCase(2, StaffHireFailure.Overflow)]
  public static void InvalidAndOverflowingPricesNeverSpend(int quantity, StaffHireFailure failure)
  {
    var currency = MakeCurrency();
    using var campaign = new GeoscapeFixture(MakeStart(staffingRules: new StaffingRulesData
    {
      ResearcherCosts = new() { [currency] = ulong.MaxValue },
      EngineerCosts = new() { [currency] = ulong.MaxValue },
    }, startingCurrencies: new() { [currency] = ulong.MaxValue }));
    foreach (StaffRole role in new[] { StaffRole.Researcher, StaffRole.Engineer })
    {
      Assert.Equal(failure, campaign.Session.GetHiringOption(role, quantity).RequireLeft());
      Assert.Equal(failure, Hire(campaign, role, quantity).RequireLeft());
      Assert.Equal(0, Count(campaign, role));
    }
    Assert.Equal(ulong.MaxValue, campaign.State.GetCurrencyBalance(currency));
    Assert.Equal(0, campaign.Events.Count);
    // The full unsigned range remains a valid one-person payment.
    campaign.Session.HireResearchers(1).RequireRight();
    Assert.Equal(0UL, campaign.State.GetCurrencyBalance(currency));
  }

  [TestCase]
  public static void MissingRulesAndCountOverflowLeaveStateUnchanged()
  {
    using var unconfigured = new GeoscapeFixture();
    Assert.Equal(StaffHireFailure.NotConfigured, unconfigured.Session.HireResearchers(1).RequireLeft());
    Assert.Equal(StaffHireFailure.NotConfigured, unconfigured.Session.HireEngineers(1).RequireLeft());
    using var campaign = new GeoscapeFixture(MakeStart(staffingRules: new StaffingRulesData
    { ResearcherBonusUnits = uint.MaxValue }));
    campaign.Session.HireResearchers(int.MaxValue).RequireRight();
    Assert.Equal(9223372030412334865L, campaign.State.Staff.ResearchRateUnits);
    Assert.Equal(StaffHireFailure.Overflow, campaign.Session.HireResearchers(1).RequireLeft());
    Assert.Equal(int.MaxValue, campaign.State.Staff.Researchers);
    Assert.Equal(1, campaign.Events.Count);
  }

  [TestCase(StaffRole.Researcher, false)]
  [TestCase(StaffRole.Engineer, false)]
  [TestCase(StaffRole.Researcher, true)]
  [TestCase(StaffRole.Engineer, true)]
  public static void HireUsesOldRateForElapsedWorkAndNewRateOnlyForRemainingWork(StaffRole role, bool inCallback)
  {
    var recipe = MakeManufacturingRecipe(MakeItemData());
    var project = MakeResearch();
    using var campaign = new GeoscapeFixture(MakeStart(staffingRules: new StaffingRulesData(),
      manufacturingRecipes: [recipe], researchProjects: [project]));
    campaign.Session.StartManufacturing(recipe).RequireRight();
    campaign.Session.StartResearch(project).RequireRight();
    if (inCallback)
      campaign.Session.EventCommitted += e =>
      {
        if (e is TimeAdvanced { Tick: 720 }) Hire(campaign, role, 5).RequireRight();
      };
    campaign.AdvanceTicks(720);
    if (!inCallback) Hire(campaign, role, 5).RequireRight();
    Assert.Equal(role == StaffRole.Researcher ? 1200L : 1440L, campaign.Session.ActiveResearch.RequireSome().CompletesAtTick);
    Assert.Equal(role == StaffRole.Engineer ? 1200L : 1440L, campaign.Session.ActiveManufacturing.RequireSome().CompletesAtTick);
    var rebuilt = new GeoscapeSession(campaign.State);
    rebuilt.ChangeSpeed(TimeSpeed.Normal);
    rebuilt.Advance(479 * 0.1);
    Assert.True(rebuilt.ActiveResearch.IsSome && rebuilt.ActiveManufacturing.IsSome);
    rebuilt.Advance(0.1);
    Assert.Equal(role != StaffRole.Researcher, rebuilt.ActiveResearch.IsSome);
    Assert.Equal(role != StaffRole.Engineer, rebuilt.ActiveManufacturing.IsSome);
    Assert.Equal(role == StaffRole.Engineer, campaign.State.Armory.HasAvailableItem(recipe.Item));
    Assert.Equal(role == StaffRole.Researcher, campaign.State.Research.IsCompleted(project));
  }

  [TestCase]
  public static void CallbackStartGetsNoRetroactiveWorkAndPositiveWorkNeedsAnotherTick()
  {
    var project = MakeResearch();
    using var campaign = new GeoscapeFixture(MakeStart(staffingRules: new StaffingRulesData
    { ResearcherBonusUnits = uint.MaxValue }, researchProjects: [project]));
    campaign.Session.HireResearchers(1).RequireRight();
    campaign.Session.EventCommitted += e =>
    {
      if (e is TimeAdvanced { Tick: 1 }) campaign.Session.StartResearch(project).RequireRight();
    };
    campaign.AdvanceTicks(1);
    Assert.Equal(2L, campaign.Session.ActiveResearch.RequireSome().CompletesAtTick);
    campaign.AdvanceTicks(1);
    Assert.True(campaign.State.Research.IsCompleted(project));
  }

  [TestCase]
  public static void SubscriberExceptionRetainsPurchasedStaff()
  {
    var currency = MakeCurrency();
    using var campaign = new GeoscapeFixture(MakeStart(staffingRules: new StaffingRulesData
    { EngineerCosts = new() { [currency] = 7 } }, startingCurrencies: new() { [currency] = 21 }));
    campaign.Session.EventCommitted += e => { if (e is StaffHired) throw new InvalidOperationException("Subscriber"); };
    Assert.Throws<InvalidOperationException>(() => campaign.Session.HireEngineers(2));
    Assert.Equal(2, campaign.State.Staff.Engineers);
    Assert.Equal(7UL, campaign.State.GetCurrencyBalance(currency));
  }

  private static int Count(GeoscapeFixture campaign, StaffRole role)
    => role == StaffRole.Researcher ? campaign.State.Staff.Researchers : campaign.State.Staff.Engineers;

  private static Either<StaffHireFailure, StaffHired> Hire(GeoscapeFixture campaign, StaffRole role, int quantity)
    => role == StaffRole.Researcher ? campaign.Session.HireResearchers(quantity) : campaign.Session.HireEngineers(quantity);
}
