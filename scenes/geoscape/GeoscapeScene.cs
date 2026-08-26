using FunProject.Strategic;
using Godot;
using System;

// Composition root for the geoscape: builds the session from the authored map, wires the
// authored map control (region visuals live in the scene), owns the code-built children,
// feeds frame deltas to the clock.
public sealed partial class GeoscapeScene : Control
{
  [Export] public GeoscapeMapData? Map;

  private GeoscapeSession _session = null!;
  private GeoscapeMapControl _map = null!;
  private GeoscapeCameraRig _camera = null!;
  private GeoscapeHud _hud = null!;
  private GeoscapeEventResolution _resolution = null!;

  public override void _Ready()
  {
    _session = new GeoscapeSession(Map ?? throw new InvalidOperationException(
      "GeoscapeScene requires a GeoscapeMapData export; assign one in the inspector."));

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
  }

  public override void _PhysicsProcess(double delta)
  {
    _session.Advance(delta);
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
