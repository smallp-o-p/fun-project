using FunProject.Dialogue;
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
public partial class GeoscapeScene : Control
{
  [Export] public CampaignStartData? Start;
  [Export] public PackedScene? ResolutionViewScene { get; set; }
  [Export] public PackedScene? DialogueViewScene { get; set; }

  private CampaignGameState _state = null!;
  private GeoscapeSession _session = null!;
  private GeoscapeMapControl _map = null!;
  private GeoscapeCameraRig _camera = null!;
  private Viewport _mapViewport = null!;
  private SubViewportContainer _mapViewportContainer = null!;
  private Control _bottomHud = null!;
  private Control _topHud = null!;
  private GeoscapeHud _hud = null!;
  private PackedScene _resolutionViewScene = null!;
  private PackedScene _dialogueViewScene = null!;
  private GeoscapeViewManager _viewManager = null!;

  public override void _Ready()
  {
    CampaignStartData start = Start ?? throw new InvalidOperationException(
      "GeoscapeScene requires a CampaignStartData export; assign one in the inspector.");
    _resolutionViewScene = ResolutionViewScene ?? throw new InvalidOperationException(
      "GeoscapeScene requires a ResolutionViewScene export; assign one in the inspector.");
    _dialogueViewScene = DialogueViewScene ?? throw new InvalidOperationException(
      "GeoscapeScene requires a DialogueViewScene export; assign one in the inspector.");
    _state = CreateGameState(start);
    _session = new GeoscapeSession(_state);

    _map = GetNode<GeoscapeMapControl>("%Map");
    _map.Setup(_session, start.Map.Size);

    // The camera stays in the full map SubViewport so it never transforms other
    // screens. Its framing keeps map markers between both HUD rows in viewport space.
    _hud = GetNode<GeoscapeHud>("%GeoscapeHud");
    _bottomHud = _hud.GetNode<Control>("BottomBar");
    _topHud = _hud.GetNode<Control>("TimePanel");
    _mapViewport = _map.GetViewport();
    _mapViewportContainer = (SubViewportContainer)_mapViewport.GetParent();
    _camera = new GeoscapeCameraRig { Name = "Camera" };
    _mapViewport.AddChild(_camera);
    _camera.Setup(start.Map.Size); // authored map bounds are a presentation concern
    _mapViewport.SizeChanged += ReframeMapCamera;
    _bottomHud.ItemRectChanged += ReframeMapCamera;
    _topHud.ItemRectChanged += ReframeMapCamera;
    ReframeMapCamera();

    _map.RegionClicked += region => GD.Print(region.FlavorText);

    _session.EventCommitted += HandleSessionEvent;

    _hud.ChangeSpeed += _session.ChangeSpeed;
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

  public override void _ExitTree()
  {
    if (IsInstanceValid(_mapViewport))
      _mapViewport.SizeChanged -= ReframeMapCamera;
    if (IsInstanceValid(_bottomHud))
      _bottomHud.ItemRectChanged -= ReframeMapCamera;
    if (IsInstanceValid(_topHud))
      _topHud.ItemRectChanged -= ReframeMapCamera;
  }

  private void ReframeMapCamera()
  {
    if (!_camera.IsInsideTree() || _mapViewportContainer.Size.Y <= 0)
      return;

    // Both HUD and container are in the outer canvas; convert to container-local
    // coordinates before accounting for the inner viewport's render size. This keeps
    // root content scaling and SubViewport stretch out of the camera's world math.
    Transform2D toContainer = _mapViewportContainer.GetGlobalTransformWithCanvas().AffineInverse();
    Vector2 bottomHudTop = toContainer * _bottomHud.GetGlobalTransformWithCanvas().Origin;
    Vector2 topHudBottom = toContainer
      * (_topHud.GetGlobalTransformWithCanvas() * new Vector2(0, _topHud.Size.Y));
    Vector2 viewportSize = _mapViewport.GetVisibleRect().Size;
    float ratio = viewportSize.Y / _mapViewportContainer.Size.Y;
    float top = Mathf.Clamp(topHudBottom.Y * ratio, 0, viewportSize.Y);
    float bottom = Mathf.Clamp(bottomHudTop.Y * ratio, top, viewportSize.Y);
    _camera.FitToArea(new Rect2(new Vector2(0, top), new Vector2(viewportSize.X, bottom - top)));
  }

  // Construction hook for derived scenes (debug playtests): the state exists before the
  // session, map, HUD and views, so overrides can stamp it freely while _Ready's wiring
  // above stays untouched.
  protected virtual CampaignGameState CreateGameState(CampaignStartData start) => new(start);

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
        break;
      case ScheduledEventFired or EventExpired:
        _map.RefreshEvents();
        break;
      case ResolutionEventOpened opened:
        var resolution = InstantiateResolutionView();
        resolution.Resolved += _session.CompleteResolution;
        _viewManager.Push(resolution); // ViewChanged presents from the now-pending session state
        PushEventDialogue(opened.Pending);
        break;
      case ResolutionEventClosed:
        // The dialog's buttons are the only close path, so it is the stack top here; the
        // type check keeps a stray close from popping an innocent view.
        if (_viewManager.Current is GeoscapeEventResolution)
          _viewManager.Pop();
        _map.RefreshEvents();
        break;
    }
  }

  // A fresh dialog per pending resolution: the manager frees popped views.
  private GeoscapeEventResolution InstantiateResolutionView()
    => InstantiateRoot<GeoscapeEventResolution>(_resolutionViewScene, "ResolutionViewScene");

  private T InstantiateRoot<T>(PackedScene scene, string exportName) where T : Node
  {
    Node instance = scene.Instantiate();
    if (instance is not T view)
    {
      string kind = instance.GetClass();
      instance.Free(); // free now: rejected roots must not linger to frame end
      throw new InvalidOperationException($"{exportName} root must be a {typeof(T).Name}; got {kind}.");
    }
    return view;
  }

  // A dialogue-carrying event plays its conversation over the just-pushed resolution
  // view: the dialogue covers it and pops back to the retained dialog when finished.
  private void PushEventDialogue(PendingResolution pending)
  {
    DialogueSequenceData? dialogue = pending.Event.Definition.Dialogue;
    if (dialogue is null)
      return;

    DialogueView view = InstantiateRoot<DialogueView>(_dialogueViewScene, "DialogueViewScene");
    view.Configure(dialogue);
    _viewManager.Push(view);
  }

  private void OpenResolution(GeoscapeEvent active)
  {
    if (_session.PendingResolution.IsNone)
      _session.OpenResolution(active);
  }
}
