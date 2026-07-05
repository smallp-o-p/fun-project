using FunProject.Battle;
using FunProject.Combatants;
using FunProject.Stats;
using Godot;
using System.Collections.Generic;

// Minimal scaffolding host: boots a small BattleRuntime via BattleFactory, builds the scene tree in
// code (camera, light, ground, unit meshes, HUD), binds the event signal handler, and repaints unit
// meshes when UnitMoved events commit. Deliberately throwaway — to be superseded by the real
// BattleScene/HUD later.
public sealed partial class BattleScene : Node3D
{
  private static readonly Vector3I BoardDimensions = new(8, 1, 8);

  private BattleRuntime _runtime = null!;
  private Faction _playerFaction = null!;
  private readonly List<Faction> _factions = [];
  private readonly Dictionary<BattleUnitState, Node3D> _unitMeshes = [];

  public override void _Ready()
  {
    _runtime = BuildRuntime();

    BuildEnvironment();
    Camera3D camera = BuildCamera();
    SpawnUnitMeshes();

    var signals = new BattleEventSignalHandler { Name = "BattleEventSignalHandler" };
    AddChild(signals);
    signals.PresentationEventCommitted += OnPresentationEvent;
    signals.Bind(_runtime);

    var movementLine = new MovementLine { Name = "MovementLine" };
    AddChild(movementLine);
    movementLine.Visible = false; // MovementLine._Ready rebuilds + shows a default line; hide it after AddChild
    var highlighter = new ReachableTileHighlighter { Name = "ReachableTileHighlighter" };
    AddChild(highlighter);

    var controller = new PlayerActionController(_runtime, _playerFaction);
    var input = new BattleInputController { Name = "BattleInputController" };
    AddChild(input);
    (Container verbButtons, Label hitChanceLabel, Button confirm, Button cancel) = BuildHud(input);
    input.Initialize(controller, camera, movementLine, highlighter, verbButtons, hitChanceLabel);
    confirm.Pressed += input.OnConfirmPressed;
    cancel.Pressed += input.OnCancelPressed;
  }

  public override void _ExitTree()
  {
    _runtime?.Dispose();
  }

  private BattleRuntime BuildRuntime()
  {
    var player = new Faction(new FactionData { Name = "Player", Description = "Player" });
    var enemy = new Faction(new FactionData { Name = "Enemy", Description = "Enemy" });
    _playerFaction = player;
    _factions.Add(player);
    _factions.Add(enemy);

    var board = new BattleBoardState(BoardDimensions);
    var placements = new List<UnitPlacement>
    {
      new(new UnitLoadout(MakeCombatant("Hero", player)), new Vector3I(1, 0, 1)),
      new(new UnitLoadout(MakeCombatant("Goon", enemy)), new Vector3I(6, 0, 6)),
    };
    var objectives = new Dictionary<Faction, IReadOnlyList<Objective>>
    {
      [player] = [new EliminateAllOpposingForcesObjective(new ObjectiveData())],
      [enemy] = [new EliminateAllOpposingForcesObjective(new ObjectiveData())],
    };

    return BattleFactory.Start(new BattleSetup(board, [player, enemy], placements, objectives))
      .Match(
        Right: runtime => runtime,
        Left: failure => throw new System.InvalidOperationException($"Battle setup failed: {failure.Message}"));
  }

  private static Combatant MakeCombatant(string name, Faction faction) =>
    new(new CombatantData
    {
      Name = name,
      HealthStat = new HealthStat { BaseValue = 20 },
      ActionPointsStat = new ActionPointsStat { BaseValue = 6 },
      WillStat = new WillStat { BaseValue = 50 },
      MovementStat = new MovementStat { BaseValue = 12 },
      VisionStat = new VisionStat { BaseValue = 20 },
      AimStat = new AimStat { BaseValue = 65 },
      ModSlotCount = 0,
    }, faction);

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
        _unitMeshes[unit.State] = mesh;
      }
    }
  }

  private void OnPresentationEvent(BattleEventAdapter adapter)
  {
    if (adapter.BattleEvent is UnitMovedBattleEvent moved && _unitMeshes.TryGetValue(moved.Unit, out Node3D? mesh))
      mesh.Position = BoardCoordinates.TileToWorldCenter(moved.Position.Raw) + new Vector3(0f, 0.5f, 0f);
  }

  private (Container VerbButtons, Label HitChance, Button Confirm, Button Cancel) BuildHud(BattleInputController input)
  {
    var layer = new CanvasLayer { Name = "Hud" };
    AddChild(layer);

    var box = new VBoxContainer { Position = new Vector2(20f, 20f) };
    layer.AddChild(box);

    var verbButtons = new VBoxContainer { Name = "VerbButtons" };
    box.AddChild(verbButtons);

    var confirm = new Button { Text = "Confirm" };
    box.AddChild(confirm);
    var cancel = new Button { Text = "Cancel" };
    box.AddChild(cancel);

    var hitChance = new Label { Name = "HitChance", Visible = false };
    box.AddChild(hitChance);

    return (verbButtons, hitChance, confirm, cancel);
  }
}
