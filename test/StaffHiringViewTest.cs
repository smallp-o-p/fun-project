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
    var input = quantity.GetLineEdit();
    input.Text = "6";
    input.EmitSignal(LineEdit.SignalName.TextChanged, input.Text);
    Assert.True(hire.Disabled, "Pending typed quantity must disable hiring before submission.");
    Assert.True(hiring.GetNode<Label>("%HireCosts").Text.Contains("18", System.StringComparison.Ordinal));
    foreach (string invalid in new[] { "", "0", "-1", "1.5", "abc", "2147483648" })
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
    Assert.Equal(engineer ? 3 : 0, campaign.State.Staff.Engineers);
    Assert.Equal(engineer ? 0 : 3, campaign.State.Staff.Researchers);
    Assert.Equal(6UL, campaign.State.GetCurrencyBalance(currency));
    Assert.True(hiring.GetNode<Label>("%StaffCount").Text.EndsWith(": 3", System.StringComparison.Ordinal));
    Assert.True(hiring.GetNode<Label>("%WorkRate").Text.Contains("1.3x", System.StringComparison.Ordinal));
    Assert.True(view.GetNode<Label>("%ActiveJob").Text.Contains("18h 28m", System.StringComparison.Ordinal));
    Assert.True(hire.Disabled, "The purchase must immediately disable the now-unaffordable quantity.");
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
  [TestCase(false)]
  [TestCase(true)]
  public static async Task OtherCommittedPurchasesRefreshTheActiveHiringView(bool engineer)
  {
    await using var cleanup = new DeferredNodeCleanup();
    var currency = MakeCurrency();
    var recipe = MakeManufacturingRecipe(MakeItemData(), costs: new() { [currency] = 5 });
    var project = MakeResearch(costs: new() { [currency] = 5 });
    var scene = CreateGeoscapeScene(MakeStart(manufacturingRecipes: [recipe], researchProjects: [project],
      startingCurrencies: new() { [currency] = 20 }, staffingRules: new StaffingRulesData
      {
        ResearcherCosts = new() { [currency] = 3 },
        EngineerCosts = new() { [currency] = 3 },
      }));
    CreateUiViewport(scene, new Vector2I(800, 600));
    var manager = scene.GetNode<GeoscapeViewManager>("%ViewManager");
    var session = CaptureSceneSession(manager);
    var open = scene.GetNode<GeoscapeHud>("%GeoscapeHud")
      .GetNode<Button>(engineer ? "%EngineeringButton" : "%ResearchButton");
    open.EmitSignal(Button.SignalName.Pressed);
    var hiring = manager.Current.GetNode<StaffHiringControl>("%StaffHiring");
    hiring.GetNode<SpinBox>("%HireQuantity").Value = 6;
    Assert.False(hiring.GetNode<Button>("%HireButton").Disabled);
    if (engineer) Assert.True(session.StartResearch(project).IsRight);
    else Assert.True(session.StartManufacturing(recipe).IsRight);
    Assert.True(hiring.GetNode<Button>("%HireButton").Disabled);
    manager.Current.RequestBack();
    open.EmitSignal(Button.SignalName.Pressed);
    hiring = manager.Current.GetNode<StaffHiringControl>("%StaffHiring");
    Assert.False(hiring.GetNode<Button>("%HireButton").Disabled);
    hiring.GetNode<SpinBox>("%HireQuantity").Value = 6;
    Assert.True(hiring.GetNode<Button>("%HireButton").Disabled);
  }
}
