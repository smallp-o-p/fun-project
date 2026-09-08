using FunProject.Battle;
using FunProject.Combatants;
using Godot;
using System.Collections.Generic;

// Minimal scaffolding host: boots a small BattleRuntime via BattleFactory, builds the scene tree in
// code (camera, light, ground, unit meshes, HUD), and connects the presentation FSM to input and
// event playback. Deliberately throwaway — to be superseded by the real BattleScene/HUD later.
public sealed partial class BattleScene : Node3D
{
  private static readonly Vector3I BoardDimensions = new(8, 1, 8);

  private BattleRuntime _runtime = null!;
  private BattleUiController _ui = null!;
  private EventPlaybackDirector _director = null!;
  private MovementLine _movementLine = null!;
  private ReachableTileHighlighter _highlighter = null!;
  private Container _verbButtons = null!;
  private Label _hitChanceLabel = null!;
  private Label _unitStatusLabel = null!;
  private Label _bannerLabel = null!;
  private Faction _playerFaction = null!;
  private readonly List<Faction> _factions = [];

  public override void _Ready()
  {
    _runtime = BuildRuntime();

    BuildEnvironment();
    Camera3D camera = BuildCamera();

    var director = new EventPlaybackDirector { Name = "EventPlaybackDirector" };
    AddChild(director);
    director.Bind(_runtime);
    _director = director;

    SpawnUnitMeshes();

    _movementLine = new MovementLine { Name = "MovementLine" };
    AddChild(_movementLine);
    _movementLine.Visible = false; // MovementLine._Ready rebuilds + shows a default line; hide it after AddChild
    _highlighter = new ReachableTileHighlighter { Name = "ReachableTileHighlighter" };
    AddChild(_highlighter);

    _ui = new BattleUiController(_runtime, _playerFaction, () => _director.Busy);

    var input = new BattleInputController { Name = "BattleInputController" };
    AddChild(input);
    input.Initialize(_ui, camera);
    BuildHud();

    _ui.StateChanged += RefreshHud;
    _ui.ReadoutsChanged += RefreshHud;
    _ui.StateChanged += () =>
    {
      if (_ui.State == UiState.BattleOver)
        ShowBattleOverBanner();
    };
    _director.PlaybackIdle += RefreshHud;
    _director.PlaybackIdle += OnPlaybackIdle;
    input.PreviewUpdated += RenderHudPreviewOnly;

    RefreshHud();
  }

  public override void _ExitTree()
  {
    _ui?.Dispose();
    _runtime?.Dispose();
  }

  private BattleRuntime BuildRuntime()
  {
    BattleTypeData type = ResourceLoader.Load<BattleTypeData>("res://resources/battle_types/bomb_defusal.tres")
      ?? throw new System.InvalidOperationException("Could not load the bomb defusal battle type.");
    BattleRuntime runtime = BattleFactory.Start(type).Match(
      Right: started => started,
      Left: failure => throw new System.InvalidOperationException($"Battle setup failed: {failure.Message}"));

    _playerFaction = runtime.Query(new GetPlayerFactionQuery()).Match(
      Some: faction => faction,
      None: () => throw new System.InvalidOperationException("Started battle is missing a player faction."));

    _factions.Clear();
    foreach (Faction faction in runtime.Query(new GetGlobalFactionTurnOrderQuery()))
      _factions.Add(faction);

    return runtime;
  }

  private void BuildEnvironment()
  {
    var light = new DirectionalLight3D { Name = "Sun", RotationDegrees = new Vector3(-60f, -45f, 0f) };
    AddChild(light);

    float width = BoardDimensions.X;
    float depth = BoardDimensions.Z;

    var ground = new StaticBody3D { Name = "Ground" };
    AddChild(ground);
    var collision = new CollisionShape3D
    {
      Shape = new BoxShape3D { Size = new Vector3(width, 0.2f, depth) },
      Position = new Vector3(width / 2f, -0.1f, depth / 2f),
    };
    ground.AddChild(collision);
    var groundMesh = new MeshInstance3D
    {
      Mesh = new PlaneMesh { Size = new Vector2(width, depth) },
      Position = new Vector3(width / 2f, 0f, depth / 2f),
    };
    ground.AddChild(groundMesh);
  }

  private Camera3D BuildCamera()
  {
    var camera = new Camera3D { Name = "Camera" };
    var center = new Vector3(BoardDimensions.X / 2f, 0f, BoardDimensions.Z / 2f);
    camera.Position = center + new Vector3(0f, 14f, 12f);
    AddChild(camera);
    camera.LookAt(center, Vector3.Up);
    camera.Current = true;
    return camera;
  }

  private void SpawnUnitMeshes()
  {
    var capsule = new CapsuleMesh { Radius = 0.3f, Height = 1.0f };

    foreach (Faction faction in _factions)
    {
      IReadOnlyCollection<AliveUnit> units = _runtime.Query(new GetFactionAliveUnits(faction));

      var material = new StandardMaterial3D
      {
        AlbedoColor = faction == _playerFaction ? Colors.SteelBlue : Colors.IndianRed,
      };

      foreach (AliveUnit unit in units)
      {
        Vector3I tile = unit.Position.Raw;

        var mesh = new MeshInstance3D
        {
          Mesh = capsule,
          MaterialOverride = material,
          Position = BoardCoordinates.TileToWorldCenter(tile) + new Vector3(0f, 0.5f, 0f),
        };
        AddChild(mesh);
        _director.RegisterUnitMesh(unit.State, mesh);
      }
    }
  }

  private void BuildHud()
  {
    var layer = new CanvasLayer { Name = "Hud" };
    AddChild(layer);

    var box = new VBoxContainer { Position = new Vector2(20f, 20f) };
    layer.AddChild(box);

    _verbButtons = new VBoxContainer { Name = "VerbButtons" };
    box.AddChild(_verbButtons);

    var confirm = new Button { Text = "Confirm" };
    box.AddChild(confirm);
    confirm.Pressed += () => _ui.Confirm();

    var cancel = new Button { Text = "Cancel" };
    box.AddChild(cancel);
    cancel.Pressed += () => _ui.Cancel();

    _unitStatusLabel = new Label { Name = "UnitStatus" };
    box.AddChild(_unitStatusLabel);

    _hitChanceLabel = new Label { Name = "HitChance", Visible = false };
    box.AddChild(_hitChanceLabel);

    _bannerLabel = new Label
    {
      Name = "BattleOverBanner",
      Visible = false,
      Position = new Vector2(300f, 200f),
      Size = new Vector2(400f, 60f),
      HorizontalAlignment = HorizontalAlignment.Center,
      VerticalAlignment = VerticalAlignment.Center,
    };
    layer.AddChild(_bannerLabel);
  }

  internal static string FormatUnitReadout(BattleUnitState unit)
    => $"{unit.Combatant.Name}  HP {unit.CurrentHealth}/{unit.MaxHealth}  STUN {unit.CurrentStun}"
      + (unit.IsUnconscious ? "  Unconscious" : "");

  private void RefreshHud()
  {
    var queriedUnits = _runtime.Query(new GetFactionAliveUnits(_playerFaction))
      .AsValueEnumerable().Select(unit => unit.State).ToArray();
    _unitStatusLabel.Text = string.Join("\n", queriedUnits.AsValueEnumerable().Select(FormatUnitReadout).ToArray());

    foreach (Node child in _verbButtons.GetChildren())
      child.QueueFree();

    foreach (UnitActionOption option in _ui.ActionOptions)
    {
      var button = new Button
      {
        Text = LabelFor(option),
        Disabled = !option.IsAvailable,
      };
      UnitActionOption captured = option;
      button.Pressed += () => _ui.BeginAction(captured);
      _verbButtons.AddChild(button);
    }

    bool targeting = _ui.State is UiState.Targeting or UiState.TargetingLocked;
    if (targeting)
      _highlighter.Show(_ui.CandidateCells);
    else
      _highlighter.Clear();

    if (targeting)
      _ui.LastPreview.Match(RenderPreview, HidePreview);
    else
      HidePreview();
  }

  private void RenderHudPreviewOnly()
  {
    if (_ui.State is not (UiState.Targeting or UiState.TargetingLocked))
    {
      HidePreview();
      return;
    }

    _ui.LastPreview.Match(RenderPreview, HidePreview);
  }

  private void ShowBattleOverBanner()
  {
    _bannerLabel.Text = _runtime.Query(new GetFactionEndOfBattleSummary(_playerFaction)).Match(
      Right: summary => summary.Outcome == BattleOutcome.Victory ? "VICTORY" : "DEFEAT",
      Left: _ => "BATTLE OVER");
    _bannerLabel.Visible = true;
  }

  private void OnPlaybackIdle()
  {
    RefreshHud();
    if (_runtime.Query(new GetBattlePhaseQuery()) != BattlePhase.InProgress)
      return;

    Faction activeSide = _runtime.Query(new GetActiveSideQuery());
    if (ReferenceEquals(activeSide, _playerFaction))
      return;

    // THROWAWAY: automatically end enemy turns until a real enemy controller exists.
    _runtime.ExecuteAction(BattleAction.EndFactionTurn(activeSide));
  }

  private static string LabelFor(UnitActionOption option) => option switch
  {
    MoveActionOption => "Move",
    AttackActionOption => "Attack",
    PassActionOption => "Pass",
    EndTurnActionOption => "End Turn",
    ReloadActionOption => "Reload",
    _ => option.GetType().Name,
  };

  private void RenderPreview(ActionPreview preview)
  {
    switch (preview)
    {
      case PathPreview p:
        _hitChanceLabel.Visible = false;
        DrawLine(p.Path);
        break;
      case AttackPreview a:
        _movementLine.Visible = false;
        _hitChanceLabel.Text = $"{a.HitChance.FinalChance}%";
        _hitChanceLabel.Visible = true;
        break;
    }
  }

  private void HidePreview()
  {
    _movementLine.Visible = false;
    _hitChanceLabel.Visible = false;
  }

  private void DrawLine(IReadOnlyList<Vector3I> path)
  {
    if (path.Count < 2)
    {
      _movementLine.Visible = false;
      return;
    }

    _movementLine.Points = path.AsValueEnumerable()
      .Select(tile => BoardCoordinates.TileToWorldCenter(tile) + new Vector3(0f, 0.05f, 0f))
      .ToArray();
    _movementLine.Rebuild();
    _movementLine.Visible = true;
  }
}
