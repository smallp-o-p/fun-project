using FunProject.Strategic;
using FunProject.Geoscape;
using static FunProject.Tests.GeoscapeTestScenes;
using Godot;
using FunProject.Engineering;
using GdUnit4;
using System;
using System.Threading.Tasks;
using static GdUnit4.Assertions;

namespace FunProject.Tests;

[TestSuite]
[RequireGodotRuntime]
public partial class GeoscapeHudTest
{
  private static GeoscapeHud BuildHud() => AddToTree(CreateHud());

  [TestCase(0L, "2d remaining")]
  [TestCase(60L, "2d remaining")]
  [TestCase(1440L, "1d remaining")]
  [TestCase(2879L, "1d remaining")]
  [TestCase(2880L, "0d remaining")]
  [TestCase(3000L, "0d remaining")]
  public static void ManufacturingSummaryShowsOnlyCurrentJobAndRoundedUpDays(long tick, string expected)
  {
    var item = MakeItemData("Field kit");
    var itemRecipe = MakeManufacturingRecipe(item, days: 2);
    using var campaign = new GeoscapeFixture(MakeStart(manufacturingRecipes: [itemRecipe]));
    Assert.True(campaign.Session.StartManufacturing(itemRecipe).IsRight);
    var hud = BuildHud();

    hud.UpdateManufacturing(campaign.Session.ActiveManufacturing, tick);

    Assert.Equal("Field kit", hud.GetNode<Label>("%EngineeringProgress").Text);
    Assert.Equal(expected, hud.GetNode<Label>("%EngineeringRemaining").Text);
    Assert.True(hud.GetNode<Label>("%EngineeringRemaining").Visible);
  }

  [TestCase]
  public static void IdleManufacturingClearsPreviousJobAndRemainingDays()
  {
    var item = MakeItemData("Field kit");
    var itemRecipe = MakeManufacturingRecipe(item, days: 2);
    using var campaign = new GeoscapeFixture(MakeStart(manufacturingRecipes: [itemRecipe]));
    Assert.True(campaign.Session.StartManufacturing(itemRecipe).IsRight);
    var hud = BuildHud();
    hud.UpdateManufacturing(campaign.Session.ActiveManufacturing, 60);

    hud.UpdateManufacturing(None, 3000);

    Assert.Equal("No active manufacturing.", hud.GetNode<Label>("%EngineeringProgress").Text);
    Assert.Equal("", hud.GetNode<Label>("%EngineeringProgress").TooltipText);
    Assert.True(hud.HasNode("%EngineeringRemaining"), "The compact HUD needs a separate remaining-days label.");
    Assert.False(hud.GetNode<Label>("%EngineeringRemaining").Visible);
    Assert.Equal("", hud.GetNode<Label>("%EngineeringRemaining").Text);
    Assert.False(hud.HasNode("%EngineeringNotice"));
  }

  [TestCase]
  public static void HudDoesNotContainAnActiveEventsPanel()
  {
    var hud = BuildHud();
    Assert.False(hud.HasNode("AlertsPanel"));
    Assert.False(hud.HasNode("%Alerts"));
    Assert.False(hud.HasNode("%AlertScroll"));
  }

  [TestCase("%UnitsButton", typeof(UnitRoster), "res://scenes/geoscape/units/UnitRoster.tscn")]
  [TestCase("%EngineeringButton", typeof(EngineeringView), "res://scenes/geoscape/engineering/EngineeringView.tscn")]
  [TestCase("%ResearchButton", typeof(ResearchView), "res://scenes/geoscape/research/ResearchView.tscn")]
  public static async Task BoundButtonsForwardFreshViews(string buttonName, Type viewType, string scenePath)
  {
    await using var cleanup = new DeferredNodeCleanup();
    var hud = BuildHud();
    SysColGeneric.List<GeoscapeView> received = [];
    Assert.Equal(Error.Ok, hud.Connect(GeoscapeHud.SignalName.ViewRequested,
      Callable.From((GeoscapeView view) =>
      {
        AutoFree(view);
        received.Add(view);
      })));

    var button = hud.GetNode<Button>(buttonName);
    button.EmitSignal(Button.SignalName.Pressed);
    button.EmitSignal(Button.SignalName.Pressed);

    Assert.Equal(2, received.Count);
    foreach (var view in received)
    {
      Assert.True(viewType.IsInstanceOfType(view));
      Assert.Equal(scenePath, view.SceneFilePath);
    }
    Assert.False(ReferenceEquals(received[0], received[1]));
  }

  [TestCase]
  public static void MissingOrWrongViewScenesAreAuthoringErrors()
  {
    var hud = BuildHud();
    GeoscapeView? received = null;
    hud.ViewRequested += view => received = view;

    Assert.Throws<InvalidOperationException>(() => hud.RequestView(Pack(new Control { Name = "NotAView" })));

    Assert.True(received is null);
  }

  [TestCase(TestName = "UpdateClock writes the pushed day and time to the clock label")]
  public static void UpdateClockWritesLabel()
  {
    var hud = BuildHud();

    hud.UpdateClock(3, new DateTime(2087, 3, 1, 8, 0, 0));

    Assert.Equal("Day 3 08:00", hud.GetNode<Label>("%ClockLabel").Text);
  }

  [TestCase(TestName = "Speed button cycles Normal → Fast → VeryFast → VeryVeryFast → Normal")]
  public static void SpeedButtonCyclesThroughSpeeds()
  {
    var hud = BuildHud();
    var requested = new System.Collections.Generic.List<TimeSpeed>();
    hud.ChangeSpeed += requested.Add;
    var speedButton = hud.GetNode<Button>("%SpeedButton");

    speedButton.EmitSignal(BaseButton.SignalName.Pressed);
    speedButton.EmitSignal(BaseButton.SignalName.Pressed);
    speedButton.EmitSignal(BaseButton.SignalName.Pressed);
    speedButton.EmitSignal(BaseButton.SignalName.Pressed);

    Assert.Equal(4, requested.Count);
    Assert.Equal(TimeSpeed.Fast, requested[0]);
    Assert.Equal(TimeSpeed.VeryFast, requested[1]);
    Assert.Equal(TimeSpeed.VeryVeryFast, requested[2]);
    Assert.Equal(TimeSpeed.Normal, requested[3]);
    Assert.Equal("1x", speedButton.Text); // cycled back to Normal's label
  }

  [TestCase(TestName = "Pausing requests Paused; unpausing requests the last active speed")]
  public static void PauseToggleRequestsPausedThenLastSpeed()
  {
    var hud = BuildHud();
    var requested = new System.Collections.Generic.List<TimeSpeed>();
    hud.ChangeSpeed += requested.Add;
    var pauseButton = hud.GetNode<Button>("%PauseButton");

    pauseButton.EmitSignal(BaseButton.SignalName.Toggled, true);
    pauseButton.EmitSignal(BaseButton.SignalName.Toggled, false);

    Assert.Equal(2, requested.Count);
    Assert.Equal(TimeSpeed.Paused, requested[0]);
    Assert.Equal(TimeSpeed.Normal, requested[1]);
  }

  [TestCase(TestName = "Selecting a speed clears the pause toggle without requesting Paused")]
  public static void SpeedSelectionClearsPauseToggle()
  {
    var hud = BuildHud();
    var requested = new System.Collections.Generic.List<TimeSpeed>();
    hud.ChangeSpeed += requested.Add;
    var pauseButton = hud.GetNode<Button>("%PauseButton");
    pauseButton.SetPressedNoSignal(true);

    hud.GetNode<Button>("%SpeedButton").EmitSignal(BaseButton.SignalName.Pressed);

    Assert.False(pauseButton.ButtonPressed);
    Assert.Equal(1, requested.Count); // only the new speed, no stray Paused request
    Assert.Equal(TimeSpeed.Fast, requested[0]);
  }
}
