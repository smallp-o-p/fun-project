using FunProject.Combatants;
using Godot;
using System;

// NOTE: this file deliberately lives in the MAIN project (under tests/) even though it
// exists only for content testing. BombDefusalContentTest spawns `godot --headless
// --path <this project> --script res://tests/...` as a separate process because the probe
// installs a SceneTree main loop, which cannot run inside the test runtime's own
// SceneTree. A C# --script run resolves its class from the host project's compiled
// assembly, so this probe must be part of fun-project's compilation — a folder here,
// never in scripts/.

namespace FunProject.Battle;

public partial class BattleTypeContentProbe : SceneTree
{
  public override void _Initialize()
  {
    try
    {
      ProbeBombDefusal();
      ProbeSkirmish();
      Quit(0);
    }
    catch (Exception exception)
    {
      GD.PushError(exception.ToString());
      Quit(1);
    }
  }

  private static void ProbeBombDefusal()
  {
    BattleTypeData type = ResourceLoader.Load<BattleTypeData>("res://resources/battle_types/bomb_defusal.tres")
      ?? throw new InvalidOperationException("Could not load the bomb defusal battle type.");
    using BattleRuntime runtime = BattleFactory.Start(type, seed: 1).Match(
      Right: started => started,
      Left: failure => throw new InvalidOperationException($"Battle setup failed: {failure.Message}"));

    if (runtime.Query(new GetBattlePhaseQuery()) != BattlePhase.InProgress)
      throw new InvalidOperationException("Bomb defusal battle did not start in progress.");
    if (runtime.Query(new GetBattleSpecialObjectsQuery()).Count != 2)
      throw new InvalidOperationException("Bomb defusal battle did not place two bombs.");

    for (int turn = 0; turn < 12 && runtime.Query(new GetBattlePhaseQuery()) == BattlePhase.InProgress; turn++)
      runtime.ExecuteAction(BattleAction.EndFactionTurn(runtime.Query(new GetActiveSideQuery())));

    if (runtime.Query(new GetBattlePhaseQuery()) != BattlePhase.Ended)
      throw new InvalidOperationException("Bomb defusal battle did not end after the authored deadline.");

    GD.Print("BOMB_DEFUSAL_CONTENT_OK");
  }

  private static void ProbeSkirmish()
  {
    BattleTypeData type = ResourceLoader.Load<BattleTypeData>("res://resources/battle_types/skirmish.tres")
      ?? throw new InvalidOperationException("Could not load the skirmish battle type.");
    using BattleRuntime runtime = BattleFactory.Start(type, seed: 1).Match(
      Right: started => started,
      Left: failure => throw new InvalidOperationException($"Battle setup failed: {failure.Message}"));

    if (runtime.Query(new GetBattlePhaseQuery()) != BattlePhase.InProgress)
      throw new InvalidOperationException("Skirmish battle did not start in progress.");

    int units = 0;
    foreach (Faction faction in runtime.Query(new GetGlobalFactionTurnOrderQuery()))
      units += runtime.Query(new GetFactionAliveUnits(faction)).Count;
    if (units != 6)
      throw new InvalidOperationException($"Skirmish battle expected 6 units but found {units}.");

    GD.Print("SKIRMISH_CONTENT_OK");
  }
}
