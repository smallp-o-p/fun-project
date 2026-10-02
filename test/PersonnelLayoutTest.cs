using System.Threading.Tasks;
using FunProject.Geoscape;
using Godot;
using GdUnit4;
using static FunProject.Tests.GeoscapeTestScenes;

namespace FunProject.Tests;

[TestSuite]
[RequireGodotRuntime]
public class PersonnelLayoutTest
{
  [TestCase("units/UnitRoster")]
  [TestCase("units/UnitView")]
  [TestCase("units/SkillProgressionView")]
  [TestCase("squad/SquadLoadoutView")]
  [TestCase("captivity/CaptivityView")]
  [TestCase("engineering/EngineeringView")]
  public void FullScreenViewsCoverLowerTransparentDialogs(string scenePath)
  {
    var view = GD.Load<PackedScene>($"res://scenes/geoscape/{scenePath}.tscn").Instantiate<GeoscapeView>();
    try
    {
      var surface = view.GetNode<PanelContainer>("Content").GetThemeStylebox("panel");
      Assert.True(surface is StyleBoxFlat { BgColor.A: 1f },
        "Full-screen views must cover the still-visible map below a transparent mission dialog.");
    }
    finally
    {
      view.Free();
    }
  }

  [TestCase(800, 600)]
  [TestCase(1280, 720)]
  [TestCase(1920, 1080)]
  public async Task RosterKeepsRowsScrollableAndDirectInspectionReachable(int width, int height)
  {
    await using var cleanup = new DeferredNodeCleanup();
    using var fixture = new GeoscapeFixture(MakeStart(roster:
      [MakeEntry("Alpha"), MakeEntry("Bravo"), MakeEntry("Charlie"), MakeEntry("Delta"),
        MakeEntry("Echo"), MakeEntry("Foxtrot"), MakeEntry("Golf"), MakeEntry("Hotel")]));
    var view = GD.Load<PackedScene>("res://scenes/geoscape/units/UnitRoster.tscn").Instantiate<UnitRoster>();
    CreateUiViewport(view, new Vector2I(width, height));
    view.Present(fixture.State, fixture.Session);
    await WaitForLayout(view);
    var bounds = new Rect2(0, 0, width, height);
    foreach (string path in new[] { "%BackButton", "%RosterScroll", "%RosterPanel", "%RosterPresentation" })
      Assert.True(bounds.Encloses(ScreenRect(view.GetNode<Control>(path))), path);
    Assert.True(ScreenRect(view.GetNode<Control>("%RosterPresentation")).End.X
      <= ScreenRect(view.GetNode<Control>("%RosterPanel")).Position.X);
    var scroll = view.GetNode<ScrollContainer>("%RosterScroll");
    Assert.Equal(ScrollContainer.ScrollMode.Disabled, scroll.HorizontalScrollMode);
    var lastRow = view.GetNode<VBoxContainer>("%UnitLabels").GetChild<UnitLabel>(7);
    lastRow.GetNode<Button>("%ClickTarget").GrabFocus();
    await WaitForLayout(view);
    Assert.True(ScreenRect(scroll).Encloses(ScreenRect(lastRow)));
    GeoscapeView? requested = null;
    view.ViewRequested += next => requested = next;
    view.GetNode<VBoxContainer>("%UnitLabels").GetChild<UnitLabel>(0)
      .GetNode<Button>("%ClickTarget").EmitSignal(Button.SignalName.Pressed);
    Assert.True(requested is UnitView);
    requested!.Free();
  }

  [TestCase(800, 600)]
  [TestCase(1280, 720)]
  [TestCase(1920, 1080)]
  public async Task SquadShowsHorizontalCardsAndKeepsRosterAndBackInsideViewport(int width, int height)
  {
    await using var cleanup = new DeferredNodeCleanup();
    using var fixture = new GeoscapeFixture(MakeStart(roster: [MakeEntry("Alpha"), MakeEntry("Bravo")]));
    var view = CreateSquadLoadoutView();
    CreateUiViewport(view, new Vector2I(width, height));
    view.Configure("An unusually long authored mission title for a compact viewport", 3, true);
    view.Present(fixture.State, fixture.Session);
    ChooseSquadUnit(view, 0, "Alpha");
    ChooseSquadUnit(view, 1, "Bravo");
    await WaitForLayout(view);
    var bounds = new Rect2(0, 0, width, height);
    foreach (string path in new[] { "%BackButton", "%SlotsScroll", "%RosterPanel", "%Title" })
      Assert.True(bounds.Encloses(ScreenRect(view.GetNode<Control>(path))), path);
    var slots = view.GetNode<BoxContainer>("%Slots");
    Assert.False(slots.Vertical);
    Rect2 first = ScreenRect(slots.GetChild<Control>(0));
    Rect2 second = ScreenRect(slots.GetChild<Control>(1));
    Assert.True(first.End.X <= second.Position.X);
    Assert.Equal(first.Position.Y, second.Position.Y);
    Assert.Equal(3, slots.GetChildCount());
    Assert.Equal(2, view.GetSelectedCombatants().Count);
    var scroll = view.GetNode<ScrollContainer>("%SlotsScroll");
    var lastAction = slots.GetChild<Control>(2).GetNode<Button>("%ChooseUnit");
    lastAction.GrabFocus(); // keyboard focus brings the last card action into its scroll viewport
    await WaitForLayout(view);
    Assert.True(lastAction.HasFocus());
    Assert.True(ScreenRect(scroll).Encloses(ScreenRect(lastAction)));
  }

  [TestCase(800, 600)]
  [TestCase(1280, 720)]
  [TestCase(1920, 1080)]
  public async Task CaptivityKeepsListDetailsAndPreparationVisible(int width, int height)
  {
    await using var cleanup = new DeferredNodeCleanup();
    using var fixture = new GeoscapeFixture();
    fixture.State.Captivity.Add(MakeCombatant("A captive with a very long authored name", MakeFaction("Alien Raiders")));
    var view = CreateCaptivityView();
    CreateUiViewport(view, new Vector2I(width, height));
    view.Present(fixture.State, fixture.Session);
    await WaitForLayout(view);
    var bounds = new Rect2(0, 0, width, height);
    foreach (string path in new[] { "%BackButton", "%PrepareButton", "%ListScroll", "%Details", "%CaptivePresentation" })
      Assert.True(bounds.Encloses(ScreenRect(view.GetNode<Control>(path))), path);
    Assert.True(ScreenRect(view.GetNode<Control>("%ListScroll")).End.X
      < ScreenRect(view.GetNode<Control>("%Details")).Position.X);
    var row = view.GetNode<VBoxContainer>("%CaptiveList").GetChild<Button>(0);
    row.ButtonPressed = true;
    Assert.False(view.GetNode<Button>("%PrepareButton").Disabled);
    Assert.True(ScreenRect(view.GetNode<Control>("%ListScroll")).Encloses(ScreenRect(row)));
  }
}
