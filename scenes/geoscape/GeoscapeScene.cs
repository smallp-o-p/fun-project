using FunProject.GameState;
using FunProject.Strategic;
using CampaignGameState = global::FunProject.GameState.GameState;
using Godot;
using System;

// Composition root for the geoscape: builds the campaign GameState from the authored
// CampaignStartData and the session over it, wires the authored map control (region
// visuals live in the scene), owns the code-built camera, feeds frame deltas to the clock,
// and presents the active view of the stack. The map and HUD form the permanent root view;
// the manager owns the stack, this scene only routes events and presents.
public sealed partial class GeoscapeScene : Control
{
  [Export] public CampaignStartData? Start;
  [Export] public PackedScene? ResolutionViewScene { get; set; }

  private CampaignGameState _state = null!;
  private GeoscapeSession _session = null!;
  private GeoscapeMapControl _map = null!;
  private GeoscapeCameraRig _camera = null!;
  private GeoscapeHud _hud = null!;
  private PackedScene _resolutionViewScene = null!;
  private GeoscapeViewManager _viewManager = null!;

  public override void _Ready()
  {
    CampaignStartData start = Start ?? throw new InvalidOperationException(
      "GeoscapeScene requires a CampaignStartData export; assign one in the inspector.");
    _resolutionViewScene = ResolutionViewScene ?? throw new InvalidOperationException(
      "GeoscapeScene requires a ResolutionViewScene export; assign one in the inspector.");
    _state = new CampaignGameState(start);
    _session = new GeoscapeSession(_state);

    _map = GetNode<GeoscapeMapControl>("%Map");
    _map.Setup(_session, start.Map.Size);

    // The camera lives inside the map's own SubViewport so its transform never reaches
    // other screens. Setup runs now: the stretched viewport is sized on tree entry,
    // before this (parent) _Ready.
    _camera = new GeoscapeCameraRig { Name = "Camera" };
    _map.GetViewport().AddChild(_camera);
    _camera.Setup(start.Map.Size); // authored map bounds are a presentation concern

    _map.RegionClicked += region => GD.Print(region.FlavorText);

    _session.EventCommitted += HandleSessionEvent;

    _hud = GetNode<GeoscapeHud>("%GeoscapeHud");
    _hud.ChangeSpeed += _session.ChangeSpeed;
    _hud.ResolutionRequested += OpenResolution;
    _map.EventClicked += adapter => OpenResolution(adapter.Event);
    _hud.UpdateClock(_session.CurrentDay, _session.CurrentTime); // the session starts paused: no TimeAdvanced yet
    _hud.UpdateManufacturing(_session.ActiveManufacturing, _session.Tick);

    _viewManager = GetNode<GeoscapeViewManager>("%ViewManager");
    // HUD buttons produce their views; their requests route through the permanent root so
    // the manager only ever hears from stack members. ViewChanged presents the active
    // view; the root is presented once here because the manager's _Ready (and its
    // install) ran before this subscription existed.
    _hud.ViewRequested += _viewManager.RootView.RequestView;
    _viewManager.ViewChanged += view => view.Present(_state, _session);
    _viewManager.RootView.Present(_state, _session);
  }

  // Covered views freeze the clock at the composition root: "the player is browsing"
  // is presentation, not campaign truth, so the session never learns views exist (the
  // pending-resolution gate stays session-side because THAT is truth). Speed is left
  // untouched — ticking resumes at the old speed once the root is active again.
  public override void _PhysicsProcess(double delta)
  {
    if (_viewManager.Current == _viewManager.RootView)
      _session.Advance(delta);
  }

  // The sole session-event router: pushes HUD/map updates from committed events. The map
  // and HUD never subscribe to the session themselves.
  private void HandleSessionEvent(IGeoscapeEvent geoscapeEvent)
  {
    if (geoscapeEvent is TimeAdvanced or ManufacturingStarted or ManufacturingCompleted)
      _hud.UpdateManufacturing(_session.ActiveManufacturing, _session.Tick);

    switch (geoscapeEvent)
    {
      case TimeAdvanced timeAdvanced:
        _hud.UpdateClock(_session.CurrentDay, timeAdvanced.CurrentTime);
        _hud.UpdateCountdowns(_session.Tick); // countdowns tick down, list not rebuilt
        break;
      case ManufacturingCompleted manufacturing:
        _hud.ShowManufacturingCompleted(manufacturing.Job);
        break;
      case ScheduledEventFired or EventExpired:
        _map.RefreshEvents();
        _hud.RefreshAlerts(_session.ActiveEvents);
        break;
      case ResolutionEventOpened:
        var resolution = InstantiateResolutionView();
        resolution.Resolved += _session.CompleteResolution;
        _viewManager.Push(resolution); // ViewChanged presents from the now-pending session state
        _hud.RefreshAlerts(_session.ActiveEvents);
        break;
      case ResolutionEventClosed:
        // The dialog's buttons are the only close path, so it is the stack top here; the
        // type check keeps a stray close from popping an innocent view.
        if (_viewManager.Current is GeoscapeEventResolution)
          _viewManager.Pop();
        _map.RefreshEvents();
        _hud.RefreshAlerts(_session.ActiveEvents);
        break;
    }
  }

  // A fresh dialog per pending resolution: the manager frees popped views.
  private GeoscapeEventResolution InstantiateResolutionView()
  {
    Node instance = _resolutionViewScene.Instantiate();
    if (instance is not GeoscapeEventResolution view)
    {
      string kind = instance.GetClass();
      instance.Free();
      throw new InvalidOperationException(
        $"GeoscapeScene ResolutionViewScene root must be a GeoscapeEventResolution; got {kind}.");
    }
    return view;
  }

  private void OpenResolution(GeoscapeEvent active)
  {
    if (_session.PendingResolution.IsNone)
      _session.OpenResolution(active);
  }
}
