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
// ReturnRequested into the campaign return.
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

  /// <summary>Raised when the player requests the campaign return while the battle is
  /// terminal and playback is idle. Only wired meaningfully when Present allowed a return.</summary>
  public event Action? ReturnRequested;

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

  /// <summary>Releases the controller and the host-owned runtime. Idempotent: tree exit and
  /// failed pre-tree host installation both route through here, and exactly the first call
  /// disposes. The host owns the runtime once Present succeeds — disposal never applies
  /// campaign results. Shadows the GodotObject IDisposable boilerplate deliberately: callers
  /// hold this as a battle host, never as a bare GodotObject.</summary>
  public new void Dispose()
  {
    if (_disposed)
      return;
    _disposed = true;
    _ui?.Dispose();
    _runtime?.Dispose();
  }

  // Fallible presentation construction, run by the installing host inside its failure
  // recovery after tree attachment: Godot's C# callback bridge only logs _Ready exceptions,
  // so autonomous tree-entry work could never trigger an installer's catch.
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
    _director.PlaybackIdle += RefreshView;
    _director.PlaybackIdle += OnPlaybackIdle;

    RefreshView();
  }

  public override void _ExitTree()
  {
    Dispose();
  }

  // The intent is re-gated at the host: only a terminal, drained battle may request the
  // campaign return.
  private void OnReturnIntent()
  {
    if (!_allowReturn || _disposed)
      return;
    if (_runtime.Query(new GetBattlePhaseQuery()) != BattlePhase.Ended || _director.Busy)
      return;
    ReturnRequested?.Invoke();
  }

  private void RefreshView()
  {
    var queriedUnits = _runtime.Query(new GetFactionAliveUnits(_playerFaction))
      .AsValueEnumerable().Select(unit => unit.State).ToArray();
    _view.ShowUnits(queriedUnits);
    _view.ShowActionOptions(_ui.ActionOptions);

    bool targeting = _ui.State is UiState.Targeting or UiState.TargetingLocked;
    if (targeting)
      _view.ShowTargeting(_ui.CandidateCells, _ui.LastPreview);
    else
      _view.HideTargeting();

    UpdateReturnAvailability();
  }

  // Availability is a fact of phase plus an idle playback queue — computed at initial
  // presentation and after every refresh (results and playback drain included), never
  // dependent on a later BattleOver transition.
  private void UpdateReturnAvailability()
  {
    bool available = _allowReturn && !_disposed
      && _runtime.Query(new GetBattlePhaseQuery()) == BattlePhase.Ended
      && !_director.Busy;
    _view.ShowReturn(available);
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
    string text = _runtime.Query(new GetFactionEndOfBattleSummary(_playerFaction)).Match(
      Right: summary => summary.Outcome == BattleOutcome.Victory ? "VICTORY" : "DEFEAT",
      Left: _ => "BATTLE OVER");
    _view.ShowBattleOver(text);
  }

  private void OnPlaybackIdle()
  {
    RefreshView();
    if (_runtime.Query(new GetBattlePhaseQuery()) != BattlePhase.InProgress)
      return;

    Faction activeSide = _runtime.Query(new GetActiveSideQuery());
    if (ReferenceEquals(activeSide, _playerFaction))
      return;

    // THROWAWAY: automatically end enemy turns until a real enemy controller exists.
    _runtime.ExecuteAction(BattleAction.EndFactionTurn(activeSide));
  }
}
