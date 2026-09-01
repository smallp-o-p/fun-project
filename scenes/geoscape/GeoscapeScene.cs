using FunProject.GameState;
using FunProject.Strategic;
using CampaignGameState = global::FunProject.GameState.GameState;
using Godot;
using System;

// Composition root for the geoscape: builds the session from the authored map, wires the
// authored map control (region visuals live in the scene), owns the code-built children,
// feeds frame deltas to the clock.
public sealed partial class GeoscapeScene : Control
{
  [Export] public CampaignStartData? Start;

  private CampaignGameState _state = null!;
  private GeoscapeSession _session = null!;
  private GeoscapeMapControl _map = null!;
  private GeoscapeCameraRig _camera = null!;
  private GeoscapeHud _hud = null!;
  private GeoscapeEventResolution _resolution = null!;
  private GeoscapeViewManager _viewManager = null!;
  private bool _viewOpen;

  public override void _Ready()
  {
    _state = new CampaignGameState(Start ?? throw new InvalidOperationException(
      "GeoscapeScene requires a CampaignStartData export; assign one in the inspector."));
    _session = new GeoscapeSession(_state);

    _map = GetNode<GeoscapeMapControl>("%Map");
    _map.Setup(_session);

    _camera = new GeoscapeCameraRig { Name = "Camera" };
    AddChild(_camera);
    _camera.Setup(_session.MapSize);

    _map.RegionClicked += region => GD.Print(region.FlavorText);

    _session.EventCommitted += HandleSessionEvent;

    _hud = GetNode<GeoscapeHud>("%GeoscapeHud");
    _hud.ChangeSpeed += _session.ChangeSpeed;
    _hud.ResolutionRequested += OpenResolution;
    _map.EventClicked += adapter => OpenResolution(adapter.Event);
    _hud.UpdateClock(_session.CurrentDay, _session.CurrentTime); // the session starts paused: no TimeAdvanced yet

    _resolution = GetNode<GeoscapeEventResolution>("%ResolutionDialog");
    _resolution.Resolved += _session.CompleteResolution;

    _viewManager = GetNode<GeoscapeViewManager>("%ViewManager");
    _hud.ViewRequested += _viewManager.Open;
    _viewManager.ViewOpened += HandleViewOpened;
    _viewManager.ViewClosed += _ => HandleViewClosed();
  }

  // Full-screen views freeze the clock at the composition root: "the player is browsing"
  // is presentation, not campaign truth, so the session never learns views exist (the
  // pending-resolution gate stays session-side because THAT is truth). Speed is left
  // untouched — ticking resumes at the old speed on close.
  public override void _PhysicsProcess(double delta)
  {
    if (!_viewOpen)
      _session.Advance(delta);
  }

  private void HandleViewOpened(GeoscapeView view, Control instance)
  {
    _viewOpen = true;
    _hud.Visible = false; // the view brings its own header (X2 full-screen screen shape)
    if (instance is UnitRoster roster)
      roster.Present(_state);
  }

  private void HandleViewClosed()
  {
    _viewOpen = false;
    _hud.Visible = true;
  }

  // The sole session-event router: pushes HUD/map updates from committed events. The map
  // and HUD never subscribe to the session themselves.
  private void HandleSessionEvent(IGeoscapeEvent geoscapeEvent)
  {
    switch (geoscapeEvent)
    {
      case TimeAdvanced timeAdvanced:
        _hud.UpdateClock(_session.CurrentDay, timeAdvanced.CurrentTime);
        _hud.UpdateCountdowns(_session.Tick); // countdowns tick down, list not rebuilt
        break;
      case ScheduledEventFired or EventExpired:
        _map.RefreshEvents();
        _hud.RefreshAlerts(_session.ActiveEvents);
        break;
      case ResolutionEventOpened opened:
        _resolution.Present(opened.Pending, RegionSuffix(opened.Pending));
        _hud.RefreshAlerts(_session.ActiveEvents);
        break;
      case ResolutionEventClosed:
        _resolution.Dismiss();
        _map.RefreshEvents();
        _hud.RefreshAlerts(_session.ActiveEvents);
        break;
    }
  }

  private string RegionSuffix(PendingResolution pending)
  {
    return pending.Event.TargetRegionIndex.Match(
      index => $" — {_session.Regions[index].Name}",
      () => "");
  }

  private void OpenResolution(GeoscapeEvent active)
  {
    if (_session.PendingResolution.IsNone)
      _session.OpenResolution(active);
  }
}
