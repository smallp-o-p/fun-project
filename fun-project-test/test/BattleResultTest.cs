using FunProject.Battle;
using FunProject.Combatants;
using GdUnit4;
using Godot;
using System;

[TestSuite]
[RequireGodotRuntime]
public class BattleResultTest
{
  private static BattleRuntime UnwrapStart(Either<BattleSetupFailure, BattleRuntime> result) =>
    result.Match(
      Right: runtime => runtime,
      Left: failure => throw new InvalidOperationException($"Expected Start to succeed but got {failure.Reason}: {failure.Message}"));

  [TestCase(TestName = "Result query rejects an in-progress battle")]
  public void RejectsInProgress()
  {
    var player = BattleTestFactory.MakeFaction("P");
    var enemy = BattleTestFactory.MakeFaction("E");
    BattleRuntime runtime = BattleTestFactory.StartRuntime(new Vector3I(4, 1, 4),
      new StartPlacement(player, BattleTestFactory.MakeCombatant("A", player), new Vector3I(0, 0, 0)),
      new StartPlacement(enemy, BattleTestFactory.MakeCombatant("B", enemy), new Vector3I(3, 0, 3)));

    Assert.True(runtime.Query(new GetBattleResultQuery()).IsLeft);
  }

  [TestCase(TestName = "Ended battle reports outcome, counts, and object tallies")]
  public void EndedBattleCounts()
  {
    var player = BattleTestFactory.MakeFaction("P");
    var enemy = BattleTestFactory.MakeFaction("E");
    var board = new BattleBoardState(new Vector3I(4, 1, 4));

    var bombData = new BattleSpecialObjectData { Name = "Bomb" };
    bombData.Capabilities.Add(new InteractiveCapabilityData());
    bombData.Capabilities.Add(new TimedEffectCapabilityData { FireAfterTurns = 1 });

    var setup = new BattleSetup(
      board,
      [player, enemy],
      [
        new UnitPlacement(new UnitLoadout(BattleTestFactory.MakeCombatant("A", player)), new Vector3I(0, 0, 0)),
        new UnitPlacement(new UnitLoadout(BattleTestFactory.MakeCombatant("B", enemy)), new Vector3I(3, 0, 3)),
      ],
      new System.Collections.Generic.Dictionary<Faction, System.Collections.Generic.IReadOnlyList<ObjectiveData>>
      {
        [player] = [new DefuseAllBombsObjectiveData
        {
          OnFail = new EndBattleDirectiveData { Outcome = BattleOutcome.Defeat },
        }],
        [enemy] = [new FakeObjectiveData()],
      },
      Objects: [new ObjectPlacement(bombData, new Vector3I(2, 0, 2))]);

    BattleRuntime runtime = UnwrapStart(BattleFactory.Start(setup));
    runtime.RegisterHook<TurnEndedBattleEvent>(new SpecialObjectTimerSystem());

    // End the player's turn: the bomb expires (deadline 1), the objective fails → Defeat.
    runtime.ExecuteAction(BattleAction.EndFactionTurn(runtime.Query(new GetActiveSideQuery())));

    Either<BattleQueryFailure, BattleResult> result = runtime.Query(new GetBattleResultQuery());
    Assert.True(result.IsRight);
    BattleResult battleResult = result.Match(
      Right: battleResult => battleResult,
      Left: failure => throw new InvalidOperationException($"Expected a battle result but got {failure.Reason}: {failure.Message}"));
    Assert.Equal(BattleOutcome.Defeat, battleResult.Outcome);
    Assert.Equal(1, battleResult.TurnCount);
    Assert.Equal(2, battleResult.Factions.Count);
    Assert.Equal(1, battleResult.ObjectsExpired);
    Assert.Equal(0, battleResult.ObjectsInteracted);
    Assert.Equal(1, battleResult.Factions[player].Spawned);
    Assert.Equal(0, battleResult.Factions[player].Killed);
    Assert.Equal(1, battleResult.Factions[enemy].Spawned);
    Assert.Equal(0, battleResult.Factions[enemy].Killed);
  }
}
