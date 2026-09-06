using FunProject.Strategic;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;
using System;

[TestSuite]
[RequireGodotRuntime]
public class GeoscapeHudTest
{
  // The GdUnit runner's Godot instance runs with the fun-project-test subproject as its
  // res:// root, so the main project's GeoscapeHud.tscn cannot be loaded from here. The test
  // rebuilds the authored node tree (ClockLabel, PauseButton, SpeedButton, UnitsButton, Alerts) in code
  // with unique names and lets _Ready wire it — pinning the HUD's push logic. The scene
  // file's own wiring is exercised by running the game.
  private static GeoscapeHud BuildHud()
  {
    var hud = AutoFree(new GeoscapeHud());

    var topBar = new HBoxContainer { Name = "TopBar" };
    var clock = new Label { Name = "ClockLabel" };
    var pause = new Button { Name = "PauseButton" };
    var speed = new Button { Name = "SpeedButton" };
    var units = new Button { Name = "UnitsButton" };
    var alerts = new VBoxContainer { Name = "Alerts" };

    foreach (Node node in new Node[] { clock, pause, speed, units, alerts })
      node.UniqueNameInOwner = true;

    topBar.AddChild(clock);
    topBar.AddChild(pause);
    topBar.AddChild(speed);
    topBar.AddChild(units);
    hud.AddChild(topBar);
    hud.AddChild(alerts);
    foreach (Node child in topBar.GetChildren())
      child.Owner = hud;
    topBar.Owner = hud;
    alerts.Owner = hud;

    ((SceneTree)Engine.GetMainLoop()).Root.AddChild(hud); // enter tree -> _Ready wires %nodes
    return hud;
  }

  private static GeoscapeEvent MakeActive(long? expiresAtTick = null)
  {
    return new GeoscapeEvent(
      TestData.MakeEvent("Raid"),
      Option<int>.None,
      OccurredTick: 0,
      expiresAtTick.HasValue ? Some(expiresAtTick.Value) : Option<long>.None);
  }

  [TestCase(TestName = "Units button raises ViewRequested for the Units view")]
  public void UnitsButtonRaisesViewRequested()
  {
    var hud = BuildHud();

    GeoscapeView? requested = null;
    hud.ViewRequested += view => requested = view;

    hud.GetNode<Button>("%UnitsButton").EmitSignal(Button.SignalName.Pressed);

    Assert.Equal(GeoscapeView.Units, requested);
  }

  [TestCase(TestName = "UpdateClock writes the pushed day and time to the clock label")]
  public void UpdateClockWritesLabel()
  {
    var hud = BuildHud();

    hud.UpdateClock(3, new DateTime(2087, 3, 1, 8, 0, 0));

    Assert.Equal("Day 3 08:00", hud.GetNode<Label>("%ClockLabel").Text);
  }

  [TestCase(TestName = "RefreshAlerts lists active events with tick countdowns")]
  public void RefreshAlertsListsWithCountdowns()
  {
    var hud = BuildHud();

    hud.RefreshAlerts([MakeActive(expiresAtTick: 30), MakeActive()]);
    hud.UpdateCountdowns(12);

    var alerts = hud.GetNode<VBoxContainer>("%Alerts");
    Assert.Equal(2, alerts.GetChildCount());
    Assert.True(((Button)alerts.GetChild(0)).Text.Contains("Raid"));
    Assert.True(((Button)alerts.GetChild(0)).Text.Contains("18")); // 30 - 12 ticks remaining
    Assert.True(((Button)alerts.GetChild(0)).Text.Contains("min")); // one tick = one in-game minute
    Assert.False(((Button)alerts.GetChild(1)).Text.Contains("min")); // no expiry shown
  }

  [TestCase(TestName = "RefreshAlerts replaces the previous list")]
  public void RefreshAlertsReplacesList()
  {
    var hud = BuildHud();
    hud.RefreshAlerts([MakeActive(1), MakeActive(2)]);

    hud.RefreshAlerts([MakeActive(3)]);

    Assert.Equal(1, hud.GetNode<VBoxContainer>("%Alerts").GetChildCount());
  }

  [TestCase(TestName = "Pressing an alert requests that event's resolution")]
  public void AlertPressRequestsResolution()
  {
    var hud = BuildHud();
    var active = MakeActive(1);
    hud.RefreshAlerts([active]);
    hud.UpdateCountdowns(0);

    GeoscapeEvent? requested = null;
    hud.ResolutionRequested += geoscapeEvent => requested = geoscapeEvent;
    ((Button)hud.GetNode<VBoxContainer>("%Alerts").GetChild(0)).EmitSignal(BaseButton.SignalName.Pressed);

    Assert.That(requested != null);
    Assert.Equal(active, requested);
  }

  [TestCase(TestName = "Speed button cycles Normal → Fast → VeryFast → VeryVeryFast → Normal")]
  public void SpeedButtonCyclesThroughSpeeds()
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
  public void PauseToggleRequestsPausedThenLastSpeed()
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
  public void SpeedSelectionClearsPauseToggle()
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
