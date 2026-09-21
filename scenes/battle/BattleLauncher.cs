using FunProject.Battle;
using Godot;

/// <summary>
/// Standalone host that resolves the exported battle type and presents BattleScene; the future
/// geoscape handoff presents the same way.
/// </summary>
public sealed partial class BattleLauncher : Node
{
  [Export] public BattleTypeData? BattleType { get; set; }

  public override void _Ready()
  {
    BattleTypeData type = BattleType ?? throw new System.InvalidOperationException(
      "BattleLauncher requires a BattleType export; assign one in the inspector.");
    BattleSetup setup = BattleSetupResolver.Resolve(type).Match(
      Right: resolved => resolved,
      Left: failure => throw new System.InvalidOperationException($"Battle setup failed: {failure.Message}"));
    BattleRuntime runtime = BattleFactory.Start(setup).Match(
      Right: started => started,
      Left: failure => throw new System.InvalidOperationException($"Battle start failed: {failure.Message}"));
    PackedScene scene = ResourceLoader.Load<PackedScene>("res://scenes/battle/BattleScene.tscn")
      ?? throw new System.InvalidOperationException("Could not load the battle scene.");
    var battle = scene.Instantiate<BattleScene>();
    battle.Present(runtime, setup);
    AddChild(battle);
  }
}
