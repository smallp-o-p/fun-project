using FunProject.Combatants;
using FunProject.Dialogue;
using FunProject.GameState;
using FunProject.Geoscape;
using FunProject.Strategic;
using CampaignGameState = global::FunProject.GameState.GameState;
using Godot;
using System;
using System.Collections.Generic;

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
  [Export] public PackedScene? BattleScene { get; set; }

  private CampaignGameState _state = null!;
  private GeoscapeSession _session = null!;
  private GeoscapeMapControl _map = null!;
  private GeoscapeHud _hud = null!;
  private PackedScene _resolutionViewScene = null!;
  private PackedScene _dialogueViewScene = null!;
  private GeoscapeViewManager _viewManager = null!;

  // Live mission presentation: the launched handle, its battle host node, and the recorded
  // view identities (preparation plus resolution dialog) that return cleanup removes.
  private MissionBattle? _activeBattle;
  private BattleScene? _battleHost;
  private SquadLoadoutView? _deploySquad;
  private GeoscapeView? _missionSquad;
  private GeoscapeView? _missionDialog;
  private GeoscapeView? _activeResolution;
  private bool _suppressPresent;

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

    _hud = GetNode<GeoscapeHud>("%GeoscapeHud");
    // Fit once after the HUD/container's deferred layout. Native window scaling
    // handles supported resolutions; the geoscape has no player camera controls.
    CallDeferred(MethodName.FrameMapCamera, start.Map.Size);

    _map.RegionClicked += region => GD.Print(region.FlavorText);

    _session.EventCommitted += HandleSessionEvent;

    _hud.ChangeSpeed += _session.ChangeSpeed;
    _map.EventClicked += adapter => OpenResolution(adapter.Event);
    _hud.UpdateClock(_session.CurrentDay, _session.CurrentTime); // the session starts paused: no TimeAdvanced yet
    _hud.UpdateManufacturing(_session.ActiveManufacturing, _session.Tick);

    _viewManager = GetNode<GeoscapeViewManager>("%ViewManager");
    // HUD buttons produce their views; their requests route through the permanent root so
    // the manager only ever hears from stack members. ViewChanged presents the active view
    // (suppressed during mission-view teardown) and manages the active squad's deploy
    // subscription; the root is presented once here because the manager's _Ready (and its
    // install) ran before this subscription existed.
    _hud.ViewRequested += _viewManager.RootView.RequestView;
    _viewManager.ViewChanged += HandleViewChanged;
    _viewManager.RootView.Present(_state, _session);
  }

  private void FrameMapCamera(Vector2I mapSize)
  {
    var viewport = _map.GetViewport();
    var container = (SubViewportContainer)viewport.GetParent();
    var bottomHud = _hud.GetNode<Control>("BottomBar");
    var topHud = _hud.GetNode<Control>("TimePanel");
    // The HUD lives outside the map viewport; express its edges in map pixels.
    Transform2D toContainer = container.GetGlobalTransformWithCanvas().AffineInverse();
    Vector2 bottomHudTop = toContainer * bottomHud.GetGlobalTransformWithCanvas().Origin;
    Vector2 topHudBottom = toContainer
      * (topHud.GetGlobalTransformWithCanvas() * new Vector2(0, topHud.Size.Y));
    Vector2 viewportSize = viewport.GetVisibleRect().Size;
    float ratio = viewportSize.Y / container.Size.Y;
    float top = Mathf.Clamp(topHudBottom.Y * ratio, 0, viewportSize.Y);
    float bottom = Mathf.Clamp(bottomHudTop.Y * ratio, top, viewportSize.Y);
    var camera = new GeoscapeCameraRig { Name = "Camera" };
    viewport.AddChild(camera);
    camera.Setup(mapSize, new Rect2(new Vector2(0, top), new Vector2(viewportSize.X, bottom - top)));
  }

  // Presents the newly active view and keeps exactly the stack top's deploy intent bound:
  // subscribing when a squad becomes current and detaching when it is covered or popped.
  private void HandleViewChanged(GeoscapeView view)
  {
    if (_deploySquad is { } bound && !ReferenceEquals(bound, view))
    {
      bound.DeployRequested -= DeployMission;
      _deploySquad = null;
    }
    if (!_suppressPresent)
      view.Present(_state, _session);
    if (view is SquadLoadoutView squad && !ReferenceEquals(_deploySquad, squad))
    {
      squad.DeployRequested += DeployMission;
      _deploySquad = squad;
    }
  }
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
        _activeResolution = resolution;
        _viewManager.Push(resolution); // ViewChanged presents from the now-pending session state
        PushEventDialogue(opened.Pending);
        break;
      case ResolutionEventClosed:
        // The dialog's buttons are the only close path, so it is the stack top here; the
        // type check keeps a stray close from popping an innocent view. Cleared before the
        // refresh work so a throwing refresh cannot strand the stale identity.
        _activeResolution = null;
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

  // ---- Mission battle handoff ---------------------------------------------------------

  // The active preparation's launch intent: instantiate and validate the battle root before
  // the session launch so authoring faults never reach it, then hand the runtime to a live
  // host and cover the geoscape stack. Failures restore preparation and propagate.
  private void DeployMission(GeoscapeEvent mission, IReadOnlyList<Combatant> squad)
  {
    ArgumentNullException.ThrowIfNull(mission);
    ArgumentNullException.ThrowIfNull(squad);
    if (_activeBattle is not null)
      throw new InvalidOperationException("A mission battle is already presented.");

    // Read at deploy time (like the dialog's SquadViewScene): a substituted export must be
    // honored, and the root is instantiated and validated before the session launch so
    // authoring faults never reach it.
    PackedScene battleSceneExport = BattleScene ?? throw new InvalidOperationException(
      "GeoscapeScene requires a BattleScene export; assign one in the inspector.");
    BattleScene battle = InstantiateRoot<BattleScene>(battleSceneExport, "BattleScene");
    MissionBattle? handle = null;
    bool presented = false;
    try
    {
      var launch = _session.LaunchMission(mission, squad);
      if (launch.IsLeft)
      {
        battle.Free(); // no runtime exists; the preparation stays for a corrected retry
        MissionLaunchFailure failure = launch.Match(
          Right: _ => throw new InvalidOperationException("Expected a launch failure."),
          Left: value => value);
        (_deploySquad ?? throw new InvalidOperationException(
            "Deploy intent arrived with no active preparation."))
          .ShowDeploymentFailure(failure);
        return;
      }
      handle = launch.Match(
        Right: value => value,
        Left: _ => throw new InvalidOperationException("Expected a launched mission battle."));

      // Recorded for return cleanup: the mission's preparation and its resolution dialog.
      _missionSquad = _deploySquad;
      _missionDialog = _activeResolution;

      battle.Present(handle.Runtime, handle.Setup, allowReturn: true);
      presented = true; // the host owns the runtime from here on
      battle.ReturnRequested += HandleBattleReturn;
      AddChild(battle); // sibling of the view manager: hiding that never disables the battle
      battle.InitializePresentation();

      _battleHost = battle;
      _activeBattle = handle;
      _viewManager.Hide();
      _viewManager.ProcessMode = ProcessModeEnum.Disabled;
    }
    catch
    {
      battle.ReturnRequested -= HandleBattleReturn;
      if (presented)
        battle.Dispose(); // host-owned runtime; idempotent across the removal below
      else if (handle is not null)
        handle.Runtime.Dispose(); // prebinding: the runtime still belongs to this scene
      if (battle.IsInsideTree())
        RemoveChild(battle); // synchronous tree exit, then deferred free
      battle.QueueFree(); // a failed installation leaves no instantiated host behind
      if (handle is not null)
      {
        _session.AbortMissionPresentation(handle);
        _battleHost = null;
        _activeBattle = null;
        _missionSquad = null;
        _missionDialog = null;
      }
      _viewManager.Show();
      _viewManager.ProcessMode = ProcessModeEnum.Inherit;
      throw;
    }
  }

  // The presented battle's return intent. Teardown needs the association to have been
  // established before CompleteMission AND consumed by it: a stale or foreign handle, or a
  // pre-consumption failure, keeps the host live for a retry, while a post-consumption
  // subscriber failure still restores the geoscape (the exception keeps propagating).
  private void HandleBattleReturn()
  {
    MissionBattle battle = _activeBattle
      ?? throw new InvalidOperationException("Return intent arrived with no active mission battle.");
    bool associated = _session.ActiveMission.Match(
      active => ReferenceEquals(active, battle.Deployment), () => false);
    try
    {
      _session.CompleteMission(battle);
    }
    finally
    {
      bool stillAssociated = _session.ActiveMission.Match(
        active => ReferenceEquals(active, battle.Deployment), () => false);
      if (associated && !stillAssociated)
        RestoreAfterReturn();
    }
  }

  private void RestoreAfterReturn()
  {
    _activeBattle = null;
    BattleScene? host = _battleHost;
    _battleHost = null;
    if (host is null)
      return;

    host.ReturnRequested -= HandleBattleReturn;
    if (host.IsInsideTree())
      RemoveChild(host); // synchronous tree exit releases the owned runtime before any replay
    host.QueueFree();

    _viewManager.Show();
    _viewManager.ProcessMode = ProcessModeEnum.Inherit;
    PopMissionViews();
  }

  // Removes only the recorded mission views, top to bottom, without re-presenting any of
  // them (the retained dialog cannot re-present once the pending mission was consumed);
  // the root is presented exactly once after cleanup.
  private void PopMissionViews()
  {
    _suppressPresent = true;
    try
    {
      if (_missionSquad is not null && ReferenceEquals(_viewManager.Current, _missionSquad))
        _viewManager.Pop();
      if (_missionDialog is not null && ReferenceEquals(_viewManager.Current, _missionDialog))
        _viewManager.Pop();
    }
    finally
    {
      _suppressPresent = false;
    }
    _missionSquad = null;
    _missionDialog = null;
    _activeResolution = null;
    _viewManager.RootView.Present(_state, _session);
  }
}
