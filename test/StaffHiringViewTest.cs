using System.Threading.Tasks;
using FunProject.Geoscape;
using FunProject.Staffing;
using FunProject.GameState;
using GdUnit4;
using Godot;
using static FunProject.Tests.GeoscapeTestScenes;

namespace FunProject.Tests;

[TestSuite]
[RequireGodotRuntime]
public class StaffHiringViewTest
{
  [TestCase(false)]
  [TestCase(true)]
  public static async Task BuffedPreviewsMatchPaymentAndCapturedActiveWork(bool engineer)
  {
    await using var cleanup = new DeferredNodeCleanup();
    var currency = MakeCurrency();
    var start = MakeStart(manufacturingRecipes: [MakeManufacturingRecipe(MakeItemData())], researchProjects: [MakeResearch()],
      startingCurrencies: new() { [currency] = 100 }, staffingRules: new StaffingRulesData
      {
        ResearcherCosts = new() { [currency] = 5 },
        EngineerCosts = new() { [currency] = 5 },
      });
    var speed = new GlobalBuffData { Process = engineer ? GlobalProcess.ManufacturingSpeed : GlobalProcess.ResearchSpeed, Modifier = 0.5 };
    start.InitialGlobalBuffs = [speed, new GlobalBuffData { Process = GlobalProcess.HiringCost, Modifier = 0.2 }];
    using var campaign = new GeoscapeFixture(start);
    GeoscapeView view = engineer ? CreateEngineeringView() : CreateResearchView();
    CreateUiViewport(view, new Vector2I(800, 600));
    view.Present(campaign.State, campaign.Session);
    var hiring = view.GetNode<StaffHiringControl>("%StaffHiring");
    Assert.True(hiring.GetNode<Label>("%HireCosts").Text.Contains('6'));
    Assert.True(hiring.GetNode<Label>("%WorkRate").Text.Contains("1.5x", System.StringComparison.Ordinal));
    view.GetNode<VBoxContainer>(engineer ? "%ManufacturableItems" : "%ResearchProjects")
      .GetChild<Button>(0).EmitSignal(Button.SignalName.Pressed);
    var duration = view.GetNode<Label>(engineer ? "%ManufacturingDuration" : "%ResearchDuration");
    Assert.Equal("Duration: 16h 0m", duration.Text);
    view.GetNode<Button>(engineer ? "%ManufactureButton" : "%StartResearchButton").EmitSignal(Button.SignalName.Pressed);
    Assert.True(view.GetNode<Label>("%ActiveJob").Text.Contains("16h 0m", System.StringComparison.Ordinal));
    campaign.State.RemoveGlobalBuff(speed);
    view.Present(campaign.State, campaign.Session);
    // Research removes its active project from the catalog.
    if (engineer) Assert.Equal("Duration: 1d", duration.Text);
    var input = hiring.GetNode<SpinBox>("%HireQuantity").GetLineEdit();
    input.Text = "2";
    input.EmitSignal(LineEdit.SignalName.TextChanged, input.Text);
    Assert.True(hiring.GetNode<Label>("%HireCosts").Text.Contains("12", System.StringComparison.Ordinal));
    hiring.GetNode<Button>("%HireButton").EmitSignal(Button.SignalName.Pressed);
    Assert.Equal(88UL, campaign.State.GetCurrencyBalance(currency));
    Assert.True(view.GetNode<Label>("%ActiveJob").Text.Contains("15h 59m", System.StringComparison.Ordinal));
    await WaitForLayout(view);
    Assert.True(new Rect2(0, 0, 800, 600).Encloses(ScreenRect(hiring.GetNode<Button>("%HireButton"))));
  }

  [TestCase(false, 800, 600)]
  [TestCase(true, 800, 600)]
  [TestCase(false, 1600, 900)]
  [TestCase(true, 1600, 900)]
  public static async Task QuantityPaymentAffordabilityAndReopeningUseLiveCampaign(bool engineer, int width, int height)
  {
    await using var cleanup = new DeferredNodeCleanup();
    var currency = MakeCurrency();
    var recipe = MakeManufacturingRecipe(MakeItemData(), costs: new() { [currency] = 5 });
    var project = MakeResearch(costs: new() { [currency] = 5 });
    using var campaign = new GeoscapeFixture(MakeStart(manufacturingRecipes: [recipe], researchProjects: [project],
      startingCurrencies: new() { [currency] = 20 }, staffingRules: new StaffingRulesData
      {
        ResearcherCosts = new() { [currency] = 3 },
        EngineerCosts = new() { [currency] = 3 },
      }));
    GeoscapeView view = engineer ? CreateEngineeringView() : CreateResearchView();
    CreateScaledWindow(view, new Vector2I(width, height));
    view.Present(campaign.State, campaign.Session);
    var hiring = view.GetNode<StaffHiringControl>("%StaffHiring");
    var quantity = hiring.GetNode<SpinBox>("%HireQuantity");
    var hire = hiring.GetNode<Button>("%HireButton");
    quantity.Value = 6;
    Assert.False(hire.Disabled);
    view.GetNode<VBoxContainer>(engineer ? "%ManufacturableItems" : "%ResearchProjects")
      .GetChild<Button>(0).EmitSignal(Button.SignalName.Pressed);
    view.GetNode<Button>(engineer ? "%ManufactureButton" : "%StartResearchButton")
      .EmitSignal(Button.SignalName.Pressed);
    // Paying for the project must also update hiring affordability immediately.
    Assert.True(hire.Disabled);
    Assert.Equal(15UL, campaign.State.GetCurrencyBalance(currency));
    quantity.Value = 2;
    Assert.True(hiring.GetNode<Label>("%HireCosts").Text.Contains('6'));
    var input = quantity.GetLineEdit();
    input.Text = "6";
    input.EmitSignal(LineEdit.SignalName.TextChanged, input.Text);
    Assert.True(hire.Disabled, "Pending typed quantity must disable hiring before submission.");
    Assert.True(hiring.GetNode<Label>("%HireCosts").Text.Contains("18", System.StringComparison.Ordinal));
    foreach (string invalid in new[] { "", "0", "-1", "1.5", "abc", "4294967296" })
    {
      input.Text = invalid;
      input.EmitSignal(LineEdit.SignalName.TextChanged, input.Text);
      Assert.True(hire.Disabled);
      hire.EmitSignal(Button.SignalName.Pressed);
      Assert.Equal(15UL, campaign.State.GetCurrencyBalance(currency));
    }
    input.Text = "3";
    input.EmitSignal(LineEdit.SignalName.TextChanged, input.Text);
    Assert.False(hire.Disabled);
    hire.EmitSignal(Button.SignalName.Pressed);
    Assert.Equal(engineer ? 3UL : 0UL, campaign.State.Staff.Engineers);
    Assert.Equal(engineer ? 0UL : 3UL, campaign.State.Staff.Researchers);
    Assert.Equal(6UL, campaign.State.GetCurrencyBalance(currency));
    Assert.True(hiring.GetNode<Label>("%StaffCount").Text.EndsWith(": 3", System.StringComparison.Ordinal));
    Assert.True(hiring.GetNode<Label>("%WorkRate").Text.Contains("1.003x", System.StringComparison.Ordinal));
    Assert.True(view.GetNode<Label>("%ActiveJob").Text.Contains("23h 56m", System.StringComparison.Ordinal));
    Assert.True(hire.Disabled, "The purchase must immediately disable the now-unaffordable quantity.");
    hire.EmitSignal(Button.SignalName.Pressed);
    Assert.Equal(6UL, campaign.State.GetCurrencyBalance(currency));
    await WaitForLayout(view);
    var visible = ScreenRect(view);
    Assert.True(visible.Encloses(ScreenRect(hire)));
    Assert.True(visible.Encloses(ScreenRect(quantity)));
    Assert.True(visible.Encloses(ScreenRect(view.GetNode<Button>("%BackButton"))));
    GeoscapeView reopened = engineer ? CreateEngineeringView() : CreateResearchView();
    CreateScaledWindow(reopened, new Vector2I(width, height));
    reopened.Present(campaign.State, campaign.Session);
    Assert.True(reopened.GetNode<StaffHiringControl>("%StaffHiring").GetNode<Label>("%StaffCount")
      .Text.EndsWith(": 3", System.StringComparison.Ordinal));
  }
}
