using FunProject.Battle;
using Godot;

/// <summary>
/// Standalone host that resolves the exported battle type and presents BattleScene; the geoscape
/// handoff presents with allowReturn true instead. The runtime is caller-owned until Present
/// succeeds, so load/instantiation/binding failures free it here; afterwards the host owns it.
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
    BattleScene? battle = null;
    bool presented = false;
    try
    {
      PackedScene scene = ResourceLoader.Load<PackedScene>("res://scenes/battle/BattleScene.tscn")
        ?? throw new System.InvalidOperationException("Could not load the battle scene.");
      Node instance = scene.Instantiate();
      if (instance is not BattleScene battleRoot)
      {
        string kind = instance.GetClass();
        instance.Free();
        throw new System.InvalidOperationException(
          $"The battle scene root must be a BattleScene; got {kind}.");
      }
      battle = battleRoot;
      battle.Present(runtime, setup); // allowReturn stays false: standalone battles end here
      presented = true; // the host owns the runtime once Present succeeds
      AddChild(battle);
      battle.InitializePresentation();
    }
    catch
    {
      if (presented)
        battle!.Dispose(); // host-owned runtime; idempotent across the removal below
      else
        runtime.Dispose(); // never bound to a host
      if (battle is not null)
      {
        if (battle.IsInsideTree())
          RemoveChild(battle);
        battle.QueueFree(); // a failed installation leaves no instantiated host behind
      }
      throw;
    }
  }
}
