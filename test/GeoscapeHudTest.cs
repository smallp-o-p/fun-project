using FunProject.Strategic;
using static FunProject.Tests.GeoscapeTestScenes;
using Godot;
using GdUnit4;
using System;
using System.Threading.Tasks;
using static GdUnit4.Assertions;

[TestSuite]
[RequireGodotRuntime]
public partial class GeoscapeHudTest
{
  internal sealed partial class ProbeView : GeoscapeView;

  private static GeoscapeHud BuildHud() => AddToTree(CreateHud());

  // Synthetic packing stays local to this suite: polymorphic navigation (an ordinary
  // button bound to a non-HUD-authored view) and invalid RequestView roots.
  private static PackedScene Pack(Node prototype)
  {
    var packed = new PackedScene();
    Error error = packed.Pack(prototype);
    prototype.Free();
    if (error != Error.Ok)
      throw new InvalidOperationException($"Test scene packing failed: {error}");
    return packed;
  }

  [TestCase]
  public void ManufacturingCountdownRendersFromPushedTick()
  {
    var item = MakeItemData("Field kit", manufacturingDays: 2);
    using var campaign = new GeoscapeFixture(MakeStart(manufacturableItems: [item]));
    var session = campaign.Session;
    Assert.True(session.StartManufacturing(item).IsRight);
    var hud = AddToTree(CreateHud());

    hud.UpdateManufacturing(session.ActiveManufacturing, 60);

    string engineering = hud.GetNode<Label>("%EngineeringProgress").Text;
    Assert.True(engineering.Contains("Field kit"));
    Assert.True(engineering.Contains("1d 23h 0m"));

    hud.UpdateManufacturing(None, 60);
    Assert.Equal("No active manufacturing.", hud.GetNode<Label>("%EngineeringProgress").Text);
  }

  [TestCase]
  public void ManufacturingCompletionNoticeSurvivesProjectRefreshes()
  {
    var item = MakeItemData("Field kit");
    using var campaign = new GeoscapeFixture(MakeStart(manufacturableItems: [item]));
    var session = campaign.Session;
    Assert.True(session.StartManufacturing(item).IsRight);
    var hud = AddToTree(CreateHud());

    session.ActiveManufacturing.IfSome(hud.ShowManufacturingCompleted);
    string notice = hud.GetNode<Label>("%EngineeringNotice").Text;
    Assert.True(notice.Contains("Field kit"));

    hud.UpdateManufacturing(session.ActiveManufacturing, 60);
    hud.UpdateManufacturing(None, 3000);
    Assert.Equal(notice, hud.GetNode<Label>("%EngineeringNotice").Text);
  }

  private static GeoscapeEvent MakeActive(long? expiresAtTick = null)
  {
    return new GeoscapeEvent(
      TestData.MakeEvent("Raid"),
      Option<int>.None,
      OccurredTick: 0,
      expiresAtTick.HasValue ? Some(expiresAtTick.Value) : Option<long>.None);
  }

  [TestCase(TestName = "Ordinary bound buttons forward fresh authored views through the signal")]
  public async Task BoundButtonsForwardFreshViews()
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

    hud.GetNode<Button>("%UnitsButton").EmitSignal(Button.SignalName.Pressed);
    hud.GetNode<Button>("%UnitsButton").EmitSignal(Button.SignalName.Pressed);
    hud.GetNode<Button>("%EngineeringButton").EmitSignal(Button.SignalName.Pressed);

    Assert.Equal(3, received.Count);
    Assert.True(received[0] is UnitRoster);
    Assert.Equal("res://scenes/geoscape/units/UnitRoster.tscn", received[0].SceneFilePath);
    Assert.True(received[1] is UnitRoster);
    Assert.Equal("res://scenes/geoscape/units/UnitRoster.tscn", received[1].SceneFilePath);
    Assert.False(ReferenceEquals(received[0], received[1])); // each Units press builds a fresh roster
    Assert.True(received[2] is EngineeringView);
    Assert.Equal("res://scenes/geoscape/engineering/EngineeringView.tscn", received[2].SceneFilePath);
  }

  [TestCase(TestName = "An arbitrary ordinary button can bind another view")]
  public void AddedOrdinaryButtonBindsAnotherViewWithoutHudChanges()
  {
    var hud = CreateHud();
    var third = new Button { Name = "ThirdButton" };
    PackedScene probeScene = Pack(new ProbeView());
    Error connection = third.Connect(Button.SignalName.Pressed,
      Callable.From(() => hud.RequestView(probeScene)));
    Assert.Equal(Error.Ok, connection);
    hud.GetNode<HBoxContainer>("%ViewButtons").AddChild(third);
    AddToTree(hud);
    GeoscapeView? received = null;
    Assert.Equal(Error.Ok, hud.Connect(GeoscapeHud.SignalName.ViewRequested,
      Callable.From((GeoscapeView view) => received = view)));

    third.EmitSignal(Button.SignalName.Pressed);

    Assert.True(received is ProbeView);
    AutoFree(received!);
  }

  [TestCase]
  public void MissingOrWrongViewScenesAreAuthoringErrors()
  {
    var hud = BuildHud();
    GeoscapeView? received = null;
    hud.ViewRequested += view => received = view;

    Assert.Throws<InvalidOperationException>(() => hud.RequestView(Pack(new Control { Name = "NotAView" })));

    Assert.True(received is null);
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
  public async Task RefreshAlertsReplacesList()
  {
    await using var cleanup = new DeferredNodeCleanup();
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
