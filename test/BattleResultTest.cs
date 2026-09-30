using FunProject.Battle;
using FunProject.Combatants;
using GdUnit4;
using Godot;
using System;

[TestSuite]
[RequireGodotRuntime]
public class BattleResultTest
{
  [TestCase(TestName = "Result query rejects an in-progress battle")]
  public void RejectsInProgress()
  {
    var player = TestData.MakeFaction("P");
    var enemy = TestData.MakeFaction("E");
    using var battle = BattleFixture.Started(new Vector3I(4, 1, 4),
      new UnitPlacement(new UnitLoadout(TestData.MakeCombatant("A", player)), new Vector3I(0, 0, 0)),
      new UnitPlacement(new UnitLoadout(TestData.MakeCombatant("B", enemy)), new Vector3I(3, 0, 3)));

    Assert.True(battle.Query(new GetBattleResultQuery()).IsLeft);
  }

  [TestCase(TestName = "Ended battle reports outcome, counts, and object tallies")]
  public void EndedBattleCounts()
  {
    var player = TestData.MakeFaction("P");
    var enemy = TestData.MakeFaction("E");

    var bombData = new BattleSpecialObjectData { Name = "Bomb" };
    bombData.Capabilities.Add(new InteractiveCapabilityData());
    bombData.Capabilities.Add(new TimedEffectCapabilityData { FireAfterTurns = 1 });

    var setup = TestData.MakeBattleSetup(player, enemy,
      new UnitLoadout(TestData.MakeCombatant("A", player)), new UnitLoadout(TestData.MakeCombatant("B", enemy)),
      [new DefuseAllBombsObjectiveData
      {
        OnFail = new EndBattleDirectiveData { Outcome = BattleOutcome.Defeat },
      }],
      [new FakeObjectiveData()])
      with
    { Objects = [new ObjectPlacement(bombData, new Vector3I(2, 0, 2))] };

    using var runtime = BattleFactory.Start(setup).RequireRight();
    runtime.RegisterHook<TurnEndedBattleEvent>(new SpecialObjectTimerSystem());

    // End the player's turn: the bomb expires (deadline 1), the objective fails → Defeat.
    runtime.ExecuteAction(BattleAction.EndFactionTurn(runtime.Query(new GetActiveSideQuery())));

    BattleResult battleResult = runtime.Query(new GetBattleResultQuery()).RequireRight();
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
