using System.Threading.Tasks;
using FunProject.Research;
using FunProject.Strategic;
using GdUnit4;
using Godot;
using static FunProject.Tests.GeoscapeTestScenes;

namespace FunProject.Tests;

[TestSuite]
[RequireGodotRuntime]
public class ResearchViewTest
{
  [TestCase(false)]
  [TestCase(true)]
  public void EmptyOrLockedCatalogKeepsStartDisabled(bool lockedProject)
  {
    ResearchProject[] projects = lockedProject
      ? [MakeResearch(condition: new ResearchTestCondition { Predicate = _ => false })]
      : [];
    using var campaign = new GeoscapeFixture(MakeStart(researchProjects: projects));
    var view = AddToTree(CreateResearchView());
    view.Present(campaign.State, campaign.Session);

    var rows = view.GetNode<VBoxContainer>("%ResearchProjects");
    Assert.Equal(1, rows.GetChildCount());
    Assert.Equal("No projects available to research.", OnlyChild<Label>(rows).Text);
    Assert.Equal("No active research.", view.GetNode<Label>("%ActiveJob").Text);
    Assert.True(view.GetNode<Button>("%StartResearchButton").Disabled);
  }

  [TestCase]
  public async Task RefreshAndStartOrSwitchUseCampaignTruth()
  {
    await using var cleanup = new DeferredNodeCleanup();
    bool unlocked = true;
    var first = MakeResearch("Kit", condition: new ResearchTestCondition { Predicate = _ => unlocked },
      manufacturingUnlocks: [MakeItemData("Kit reward")]);
    first.Description = "Prototype details.";
    var second = MakeResearch("Kit");
    using var campaign = new GeoscapeFixture(MakeStart(researchProjects: [first, second]));
    var view = AddToTree(CreateResearchView());
    view.Present(campaign.State, campaign.Session);
    var rows = view.GetNode<VBoxContainer>("%ResearchProjects");
    rows.GetChild<Button>(0).EmitSignal(Button.SignalName.Pressed);
    Assert.Equal("Kit", view.GetNode<Label>("%ProjectName").Text);
    Assert.Equal("Prototype details.", view.GetNode<Label>("%ProjectDescription").Text);
    Assert.Equal("Duration: 1d", view.GetNode<Label>("%ResearchDuration").Text);
    Assert.Equal("Manufacturing unlocks: Kit reward", view.GetNode<Label>("%ManufacturingUnlocks").Text);

    unlocked = false;
    var start = view.GetNode<Button>("%StartResearchButton");
    start.EmitSignal(Button.SignalName.Pressed);
    Assert.True(start.Disabled);
    Assert.Equal("Select a project to research.", view.GetNode<Label>("%ProjectName").Text);
    Assert.Equal("", view.GetNode<Label>("%ProjectDescription").Text);
    Assert.False(OnlyChild<Button>(rows).ButtonPressed);
    Assert.True(view.GetNode<Label>("%Status").Text.Contains("Kit"));
    Assert.True(view.GetNode<Label>("%Status").Text.Contains("Locked"));
    view.Present(campaign.State, campaign.Session);
    Assert.Equal("", view.GetNode<Label>("%Status").Text);

    OnlyChild<Button>(rows).EmitSignal(Button.SignalName.Pressed);
    view.Present(campaign.State, campaign.Session);
    view.Present(campaign.State, campaign.Session);
    Assert.True(OnlyChild<Button>(rows).ButtonPressed);
    start.EmitSignal(Button.SignalName.Pressed);
    Assert.True(start.Disabled);
    start.EmitSignal(Button.SignalName.Pressed);
    Assert.Equal(1, campaign.Events.EventsOf<ResearchStarted>().Length);
    Assert.True(ReferenceEquals(second, campaign.Session.ActiveResearch.RequireSome().Project));
    Assert.True(view.GetNode<Label>("%ActiveJob").Text.Contains("1d 0h 0m"));
    campaign.AdvanceTicks(1);
    view.Present(campaign.State, campaign.Session);
    Assert.True(view.GetNode<Label>("%ActiveJob").Text.Contains("23h 59m"));
    unlocked = true;
    view.Present(campaign.State, campaign.Session);
    OnlyChild<Button>(rows).EmitSignal(Button.SignalName.Pressed);
    Assert.False(start.Disabled);
    Assert.Equal("Switch Research", start.Text);
    start.EmitSignal(Button.SignalName.Pressed);
    Assert.Equal(2, campaign.Events.EventsOf<ResearchStarted>().Length);
    Assert.True(ReferenceEquals(first, campaign.Session.ActiveResearch.RequireSome().Project));
    Assert.Equal("Kit", OnlyChild<Button>(rows).Text);
    Assert.False(OnlyChild<Button>(rows).ButtonPressed);
  }

  [TestCase(800, 600)]
  [TestCase(1600, 900)]
  public async Task LongResearchDetailsKeepActionsReachable(int width, int height)
  {
    await using var cleanup = new DeferredNodeCleanup();
    var reward = MakeItemData("A research reward with a long descriptive manufacturing name");
    var project = MakeResearch(manufacturingUnlocks: [reward]);
    for (int i = 0; i < 80; i++)
      project.Description += "Research develops new equipment for Engineering and advances through campaign time on the map. ";
    using var campaign = new GeoscapeFixture(MakeStart(researchProjects: [project]));
    var view = CreateResearchView();
    CreateUiViewport(view, new Vector2I(width, height));
    view.Present(campaign.State, campaign.Session);
    OnlyChild<Button>(view.GetNode<VBoxContainer>("%ResearchProjects")).EmitSignal(Button.SignalName.Pressed);
    await WaitForLayout(view);

    var visible = new Rect2(0, 0, width, height);
    var background = view.GetNode<SubViewportContainer>("Background");
    Assert.True(background.Visible);
    Assert.Equal(visible, ScreenRect(background));
    var panel = view.GetNode<PanelContainer>("%ResearchPanel");
    Assert.True(ScreenRect(panel).Position.X >= width * 0.5f);
    Assert.True(visible.Encloses(ScreenRect(panel)));
    var start = view.GetNode<Button>("%StartResearchButton");
    var back = view.GetNode<Button>("%BackButton");
    Assert.True(visible.Encloses(ScreenRect(start)));
    Assert.True(visible.Encloses(ScreenRect(back)));
    var scroll = view.GetNode<ScrollContainer>("%DetailsScroll");
    var startRect = ScreenRect(start);
    var backRect = ScreenRect(back);
    scroll.ScrollVertical = 10000;
    await WaitForLayout(view);
    Assert.True(scroll.ScrollVertical > 0);
    Assert.True(ScreenRect(scroll).Encloses(ScreenRect(view.GetNode<Label>("%ManufacturingUnlocks"))));
    Assert.Equal(startRect, ScreenRect(start));
    Assert.Equal(backRect, ScreenRect(back));
  }
}
