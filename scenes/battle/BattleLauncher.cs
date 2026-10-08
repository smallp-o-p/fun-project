using FunProject.Battle;
using FunProject.Scenes.Ext;
using Godot;

namespace FunProject.Scenes;

/// <summary>Starts the exported battle type and owns presentation-failure cleanup.</summary>
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
    PackedScene scene = ResourceLoader.Load<PackedScene>("res://scenes/battle/BattleScene.tscn")
      ?? throw new System.InvalidOperationException("Could not load the battle scene.");
    var battle = scene.InstantiateAs<BattleScene>("The battle scene");
    try
    {
      BattleRuntime runtime = BattleFactory.Start(setup).Match(
        Right: started => started,
        Left: failure => throw new System.InvalidOperationException($"Battle start failed: {failure.Message}"));
      battle.Present(runtime, setup); // allowReturn stays false: standalone battles end here
      AddChild(battle);
      battle.InitializePresentation();
    }
    catch
    {
      battle.Dispose();
      if (battle.IsInsideTree())
        RemoveChild(battle);
      battle.QueueFree();
      throw;
    }
  }
}
