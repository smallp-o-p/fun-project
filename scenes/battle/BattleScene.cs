using FunProject.Battle;
using FunProject.Combatants;
using Godot;
using System;

// Presentation host: receives a started BattleRuntime via Present, instances the pooled battle
// map and builds unit meshes in code, keeps the presentation FSM plus refresh orchestration and
// input routing here, and pushes view models into the runtime-free view (authored BattleUI)
// while intents flow back through events. The environment (WorldEnvironment, sun, authored
// GameCamera rig instance) is authored in the scene. The standalone debug host is BattleLauncher
// (allowReturn false); the geoscape handoff presents with allowReturn true and routes
// Completed into the campaign return.
// Deliberately throwaway — to be superseded by the real BattleScene later.
public sealed partial class BattleScene : Node3D
{
  private BattleRuntime _runtime = null!;
  private BattleSetup _setup = null!;
  private BattleUiController _ui = null!;
  private EventPlaybackDirector _director = null!;
  private BattleUI _view = null!;
  private Faction _playerFaction = null!;
  private bool _allowReturn;
  private bool _disposed;
  private bool _refreshPending;
  private bool _idlePending;
  private bool _bannerPending;

  /// <summary>Requests campaign completion after terminal playback finishes.</summary>
  public event Action<FactionBattleSummary>? Completed;

  public void Present(BattleRuntime runtime, BattleSetup setup, bool allowReturn = false)
  {
    ArgumentNullException.ThrowIfNull(runtime);
    ArgumentNullException.ThrowIfNull(setup);
    if (_runtime is not null)
      throw new System.InvalidOperationException("BattleScene was already presented.");
    _runtime = runtime;
    _setup = setup;
    _allowReturn = allowReturn;
  }

  /// <summary>Releases the controller and host-owned runtime once.</summary>
  public new void Dispose()
  {
    if (_disposed)
      return;
    _disposed = true;
    _ui?.Dispose();
    _director?.Unbind();
    _runtime?.Dispose();
  }

  // The installer runs fallible setup after tree attachment and recovers exceptions directly.
  public void InitializePresentation()
  {
    if (_runtime is null || _setup is null)
      throw new System.InvalidOperationException(
        "BattleScene requires Present(runtime, setup) before initialization.");
    if (_ui is not null)
      throw new System.InvalidOperationException("BattleScene was already initialized.");

    _playerFaction = _runtime.Query(new GetPlayerFactionQuery()).Match(
      Some: faction => faction,
      None: () => throw new System.InvalidOperationException("Started battle is missing a player faction."));

    InstantiateMap();
    GameCamera rig = ConfigureGameCamera();

    var director = new EventPlaybackDirector { Name = "EventPlaybackDirector" };
    AddChild(director);
    director.Bind(_runtime);
    _director = director;

    var unitMeshes = new BattleUnitMeshes { Name = "UnitMeshes" };
    AddChild(unitMeshes);
    unitMeshes.Initialize(_runtime, _director, _playerFaction);

    _ui = new BattleUiController(_runtime, _playerFaction, () => _director.Busy);

    var input = new BattleInputController { Name = "BattleInputController" };
    AddChild(input);
    input.Initialize(rig);
    _view = GetNode<BattleUI>("%BattleUI");
    _view.ConfirmRequested += () => _ui.Confirm();
    _view.CancelRequested += _ui.Cancel;
    _view.VerbSelected += _ui.BeginAction;
    _view.ReturnRequested += OnReturnIntent;
    input.TileClicked += tile => _ui.ClickTile(tile);
    input.TileHovered += OnTileHovered;
    input.ConfirmPressed += () => _ui.Confirm();
    input.CancelPressed += _ui.Cancel;

    _ui.StateChanged += RefreshView;
    _ui.ReadoutsChanged += RefreshView;
    _ui.StateChanged += () =>
    {
      if (_ui.State == UiState.BattleOver)
        ShowBattleOverBanner();
    };
    _director.PlaybackIdle += OnPlaybackIdle;

    RefreshView();

    // Show startup-terminal results immediately.
    if (_ui.State == UiState.BattleOver)
      ShowBattleOverBanner();
  }

  public override void _ExitTree()
  {
    Dispose();
  }

  public override void _Process(double delta) => TickDeferredRefresh();

  public void TickDeferredRefresh()
  {
    if (_disposed || _ui is null) return;
    if (_runtime.IsClosing) { _refreshPending = _idlePending = _bannerPending = false; return; }
    _ui.TickDeferredRefresh();
    if (_refreshPending) RefreshView();
    if (_bannerPending) ShowBattleOverBanner();
    if (_idlePending) OnPlaybackIdle();
  }

  private bool TryRead(out BattleQueryScope? scope)
  {
    scope = null;
    if (_disposed) return false;
    var admission = _runtime.TryAcquireQueryScope();
    if (admission.IsRight) { scope = admission.Match(Right: value => value, Left: failure => throw new InvalidOperationException($"Battle access failed: {failure}.")); return true; }
    if (admission.Match(Left: failure => failure, Right: _ => throw new InvalidOperationException("Expected failed battle admission.")) == BattleAccessFailure.Busy) _refreshPending = true;
    else _refreshPending = _idlePending = _bannerPending = false;
    return false;
  }

  private bool CanReturnOwned => _allowReturn && !_disposed
    && _runtime.Query(new GetBattleOutcomeQuery()).IsSome && !_director.Busy;

  private void OnReturnIntent()
  {
    if (!TryRead(out var scope)) return;
    FactionBattleSummary? summary = null;
    using (scope)
    {
      if (CanReturnOwned)
        summary = _runtime.Query(new GetFactionEndOfBattleSummary(_playerFaction)).Match(
          Right: value => value, Left: failure => throw new InvalidOperationException(failure.Message));
    }
    if (summary is not null) Completed?.Invoke(summary);
  }

  private void RefreshView()
  {
    if (!TryRead(out var scope)) return;
    bool canReturn;
    using (scope) { canReturn = CanReturnOwned; _refreshPending = false; }
    _view.ShowUnits(_ui.UnitReadouts);
    _view.ShowActionOptions(_ui.ActionReadouts);
    if (_ui.State is UiState.Targeting or UiState.TargetingLocked)
      _view.ShowTargeting(_ui.CandidateCells, _ui.LastPreview);
    else _view.HideTargeting();
    _view.ShowReturn(canReturn);
  }

  private void InstantiateMap()
  {
    var scene = _setup.MapScene.Match(
      Some: packedScene => packedScene,
      None: () => throw new System.InvalidOperationException(
        "BattleScene requires a battle setup resolved from a map pool (MapScene was None)."));
    var map = scene.Instantiate<BattleMap>();
    map.Name = "Map";
    AddChild(map);
  }

  private GameCamera ConfigureGameCamera()
  {
    var rig = GetNode<GameCamera>("%GameCamera");
    var center = new Vector3(_setup.Map.Dimensions.X / 2f, 0f, _setup.Map.Dimensions.Z / 2f);
    rig.GlobalPosition = center + new Vector3(0f, 10f, 0f);
    return rig;
  }

  private void OnTileHovered(Vector3I tile)
  {
    if (_ui.State != UiState.Targeting)
      return;

    _ui.PreviewAt(tile);
    RefreshPreviewOnly();
  }

  private void RefreshPreviewOnly()
  {
    if (_ui.State is not (UiState.Targeting or UiState.TargetingLocked))
    {
      _view.HidePreview();
      return;
    }

    _ui.LastPreview.Match(_view.ShowPreview, _view.HidePreview);
  }

  private void ShowBattleOverBanner()
  {
    if (!TryRead(out var scope)) { if (!_runtime.IsClosing && !_disposed) _bannerPending = true; return; }
    string text;
    using (scope)
    {
      _bannerPending = false;
      text = _runtime.Query(new GetFactionEndOfBattleSummary(_playerFaction)).Match(
        Right: summary => summary.Outcome == BattleOutcome.Victory ? "VICTORY" : "DEFEAT", Left: _ => "BATTLE OVER");
    }
    _view.ShowBattleOver(text);
  }

  private void OnPlaybackIdle()
  {
    if (!TryRead(out var scope)) { if (!_runtime.IsClosing && !_disposed) _idlePending = true; return; }
    Faction? enemy = null;
    long revision;
    using (scope)
    {
      _idlePending = false;
      revision = scope!.Revision;
      if (_runtime.Query(new GetBattleOutcomeQuery()).IsNone)
      {
        var activeSide = _runtime.Query(new GetActiveSideQuery());
        if (activeSide != _playerFaction) enemy = activeSide;
      }
    }
    RefreshView();
    if (enemy is null) return;
    // Existing throwaway turn stub; no controller or policy is introduced here.
    var result = _runtime.TryExecuteAction(runtime => BattleAction.EndFactionTurn(runtime.Query(new GetActiveSideQuery())), Some(revision));
    if (result.IsLeft && result.Match(Left: failure => failure, Right: _ => throw new InvalidOperationException("Expected failed battle admission.")) != BattleAccessFailure.Closed) _idlePending = true;
  }
}
