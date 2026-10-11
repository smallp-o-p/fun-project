using System.Threading.Tasks;
using FunProject.Geoscape;
using FunProject.Staffing;
using GdUnit4;
using Godot;
using static FunProject.Tests.GeoscapeTestScenes;

namespace FunProject.Tests;

[TestSuite]
[RequireGodotRuntime]
public class StaffHiringViewTest
{
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
    CreateUiViewport(view, new Vector2I(width, height));
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
    // Apply pending typed input before submitting, including when it differs from Value.
    quantity.GetLineEdit().Text = "3";
    hire.EmitSignal(Button.SignalName.Pressed);
    Assert.Equal(engineer ? 3 : 0, campaign.State.Staff.Engineers);
    Assert.Equal(engineer ? 0 : 3, campaign.State.Staff.Researchers);
    Assert.Equal(6UL, campaign.State.GetCurrencyBalance(currency));
    Assert.True(hiring.GetNode<Label>("%StaffCount").Text.EndsWith(": 3", System.StringComparison.Ordinal));
    Assert.True(hiring.GetNode<Label>("%WorkRate").Text.Contains("1.3x", System.StringComparison.Ordinal));
    Assert.True(view.GetNode<Label>("%ActiveJob").Text.Contains("18h 28m", System.StringComparison.Ordinal));
    quantity.Value = 3;
    Assert.True(hire.Disabled);
    hire.EmitSignal(Button.SignalName.Pressed);
    Assert.Equal(6UL, campaign.State.GetCurrencyBalance(currency));
    await WaitForLayout(view);
    var visible = new Rect2(0, 0, width, height);
    Assert.True(visible.Encloses(ScreenRect(hire)));
    Assert.True(visible.Encloses(ScreenRect(quantity)));
    Assert.True(visible.Encloses(ScreenRect(view.GetNode<Button>("%BackButton"))));
    GeoscapeView reopened = engineer ? CreateEngineeringView() : CreateResearchView();
    CreateUiViewport(reopened, new Vector2I(width, height));
    reopened.Present(campaign.State, campaign.Session);
    Assert.True(reopened.GetNode<StaffHiringControl>("%StaffHiring").GetNode<Label>("%StaffCount")
      .Text.EndsWith(": 3", System.StringComparison.Ordinal));
  }
}
