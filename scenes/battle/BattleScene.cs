using FunProject.Battle;
using FunProject.Combatants;
using Godot;
using System;

// Presentation host: receives a started BattleRuntime via Present, instances the pooled battle
// map and builds unit meshes in code, keeps the presentation FSM plus refresh orchestration and
// input routing here, and pushes view models into the runtime-free view (authored BattleUI)
// while intents flow back through events. The environment (WorldEnvironment, sun, authored
// GameCamera rig instance) is authored in the scene. The standalone debug host is BattleLauncher;
// the future geoscape handoff presents the same way.
// Deliberately throwaway — to be superseded by the real BattleScene later.
public sealed partial class BattleScene : Node3D
{
  private BattleRuntime _runtime = null!;
  private BattleSetup _setup = null!;
  private BattleUiController _ui = null!;
  private EventPlaybackDirector _director = null!;
  private BattleUI _view = null!;
  private Faction _playerFaction = null!;

  public void Present(BattleRuntime runtime, BattleSetup setup)
  {
    ArgumentNullException.ThrowIfNull(runtime);
    ArgumentNullException.ThrowIfNull(setup);
    if (_runtime is not null)
      throw new System.InvalidOperationException("BattleScene was already presented.");
    _runtime = runtime;
    _setup = setup;
  }

  public override void _Ready()
  {
    if (_runtime is null || _setup is null)
      throw new System.InvalidOperationException(
        "BattleScene requires Present(runtime, setup) before entering the tree.");

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
    _ui?.Dispose();
    _runtime?.Dispose();
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
