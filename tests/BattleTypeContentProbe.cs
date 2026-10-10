using FunProject.Combatants;
using FunProject.Weapons;
using Godot;
using System;
using System.Collections.Generic;

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

    if (runtime.Query(new GetBattleOutcomeQuery()).IsSome)
      throw new InvalidOperationException("Bomb defusal battle did not start in progress.");
    if (runtime.Query(new GetBattleSpecialObjectsQuery()).Count != 2)
      throw new InvalidOperationException("Bomb defusal battle did not place two bombs.");

    Faction player = runtime.Query(new GetPlayerFactionQuery()).Match(value => value,
      () => throw new InvalidOperationException("Bomb defusal requires a player faction."));
    Faction enemy = runtime.Query(new GetGlobalFactionTurnOrderQuery()).AsValueEnumerable()
      .Single(faction => !ReferenceEquals(faction, player));
    List<AmmunitionedWeapon> pistols = [];
    foreach (AliveUnit unit in runtime.Query(new GetFactionAliveUnits(enemy)))
    {
      Weapon weapon = unit.State.EquippedWeapon.Match(value => value,
        () => throw new InvalidOperationException("Every demo grunt must carry the service pistol."));
      if (weapon is not AmmunitionedWeapon pistol
        || !string.Equals(pistol.ItemName, "Service Pistol", StringComparison.Ordinal)
        || pistol.CurrentAmmo != pistol.MagazineSize || !pistol.IsLoaded || pistols.Contains(pistol))
        throw new InvalidOperationException("Demo grunts require independent, fully loaded service pistols.");
      pistols.Add(pistol);
    }
    if (pistols.Count != 3)
      throw new InvalidOperationException("Bomb defusal expected three armed grunts.");
    if (pistols[0].TrySpendShot().IsNone
      || pistols[1].CurrentAmmo != pistols[1].MagazineSize
      || pistols[2].CurrentAmmo != pistols[2].MagazineSize)
      throw new InvalidOperationException("Spending one demo pistol must not change another's magazine.");

    for (int turn = 0; turn < 12 && runtime.Query(new GetBattleOutcomeQuery()).IsNone; turn++)
      runtime.ExecuteAction(BattleAction.EndFactionTurn(runtime.Query(new GetActiveSideQuery())));

    if (runtime.Query(new GetBattleOutcomeQuery()).IsNone)
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

    if (runtime.Query(new GetBattleOutcomeQuery()).IsSome)
      throw new InvalidOperationException("Skirmish battle did not start in progress.");

    int units = 0;
    foreach (Faction faction in runtime.Query(new GetGlobalFactionTurnOrderQuery()))
      units += runtime.Query(new GetFactionAliveUnits(faction)).Count;
    if (units != 6)
      throw new InvalidOperationException($"Skirmish battle expected 6 units but found {units}.");

    GD.Print("SKIRMISH_CONTENT_OK");
  }
}
