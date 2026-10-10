using FunProject.Battle;
using FunProject.Combatants;
using Godot;
using System;

namespace FunProject.Scenes;

// Owns the runtime and presentation lifetime. The controller owns selection/targeting;
// the HUD owns widgets; the director receives committed results before the UI reconciles them.
public sealed partial class BattleScene : Node3D
{
  private BattleRuntime _runtime = null!;
  private BattleSetup _setup = null!;
  private BattleUiController _ui = null!;
  private EventPlaybackDirector _director = null!;
  private BattleUI _view = null!;
  private BattleHud _hud = null!;
  private BattleInputController _input = null!;
  private Faction _playerFaction = null!;
  private bool _allowReturn;
  private bool _disposed;

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
    ProcessMode = ProcessModeEnum.Disabled;
    if (_director is not null)
    {
      _runtime.ActionCompleted -= _director.EnqueueResult;
      _director.PlaybackIdle -= OnPlaybackIdle;
    }
    if (_ui is not null) _ui.Changed -= RefreshView;
    if (_hud is not null)
    {
      _hud.ConfirmRequested -= OnConfirmIntent;
      _hud.CancelRequested -= OnCancelIntent;
      _hud.VerbSelected -= OnVerbIntent;
      _hud.ReturnRequested -= OnReturnIntent;
    }
    if (_input is not null)
    {
      _input.TileClicked -= OnTileClicked;
      _input.TileHovered -= OnTileHovered;
      _input.ConfirmPressed -= OnConfirmIntent;
      _input.CancelPressed -= OnCancelIntent;
    }
    _director?.Cancel();
    _ui?.Dispose();
    _runtime?.Dispose();
  }

  // The installer runs fallible setup after tree attachment and recovers exceptions directly.
  public void InitializePresentation()
  {
    ObjectDisposedException.ThrowIf(_disposed, this);
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

    _director = new EventPlaybackDirector { Name = "EventPlaybackDirector" };
    AddChild(_director);
    _runtime.ActionCompleted += _director.EnqueueResult;

    var unitMeshes = new BattleUnitMeshes { Name = "UnitMeshes" };
    AddChild(unitMeshes);
    unitMeshes.Initialize(_runtime, _director, _playerFaction);

    _ui = new BattleUiController(_runtime, _playerFaction, () => _director.Busy);

    _input = new BattleInputController { Name = "BattleInputController" };
    AddChild(_input);
    _input.Initialize(rig);
    _view = GetNode<BattleUI>("%BattleUI");
    _hud = _view.Hud ?? throw new InvalidOperationException("Battle UI requires a ready BattleHud.");
    _hud.ConfirmRequested += OnConfirmIntent;
    _hud.CancelRequested += OnCancelIntent;
    _hud.VerbSelected += OnVerbIntent;
    _hud.ReturnRequested += OnReturnIntent;
    _input.TileClicked += OnTileClicked;
    _input.TileHovered += OnTileHovered;
    _input.ConfirmPressed += OnConfirmIntent;
    _input.CancelPressed += OnCancelIntent;

    _ui.Changed += RefreshView;
    _director.PlaybackIdle += OnPlaybackIdle;

    RefreshView();
  }

  public override void _ExitTree()
  {
    Dispose();
  }

  private bool CanReturn => _allowReturn && !_disposed
    && _runtime.Query(new GetBattleOutcomeQuery()).IsSome && !_director.Busy;

  private void OnReturnIntent()
  {
    if (CanReturn)
      Completed?.Invoke(_runtime.Query(new GetFactionEndOfBattleSummary(_playerFaction)).Match(
        Right: summary => summary,
        Left: failure => throw new InvalidOperationException(failure.Message)));
  }

  private void RefreshView()
  {
    if (_disposed) return;
    if (_director.Busy)
    {
      _hud.DisableInput();
      return;
    }
    var queriedUnits = _runtime.Query(new GetFactionAliveUnits(_playerFaction))
      .AsValueEnumerable().Select(unit => unit.State).ToArray();
    _hud.ShowUnits(queriedUnits);
    _hud.ShowActionOptions(_ui.ActionOptions, _ui.Gate == InputGate.Open);

    bool targeting = _ui.State is UiState.Targeting or UiState.TargetingLocked;
    if (targeting)
      _view.ShowTargeting(_ui.CandidateCells, _ui.LastPreview);
    else
      _view.HideTargeting();

    _hud.ShowReturn(CanReturn);
    if (_ui.State == UiState.BattleOver)
      ShowBattleOverBanner();
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
    if (_disposed || _ui.Gate != InputGate.Open || _ui.State != UiState.Targeting)
      return;

    _ui.PreviewAt(tile);
    _ui.LastPreview.Match(_view.ShowPreview, _view.HidePreview);
  }

  private void ShowBattleOverBanner()
  {
    string text = _runtime.Query(new GetFactionEndOfBattleSummary(_playerFaction)).Match(
      Right: summary => summary.Outcome == BattleOutcome.Victory ? "VICTORY" : "DEFEAT",
      Left: _ => "BATTLE OVER");
    _hud.ShowBattleOver(text);
  }

  private void OnConfirmIntent()
  {
    if (!_disposed) _ui.Confirm();
  }

  private void OnCancelIntent() { if (!_disposed) _ui.Cancel(); }
  private void OnVerbIntent(UnitActionOption option) { if (!_disposed) _ui.BeginAction(option); }
  private void OnTileClicked(Vector3I tile) { if (!_disposed) _ui.ClickTile(tile); }
  private void OnPlaybackIdle() => RefreshView();
}
