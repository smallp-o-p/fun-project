using FunProject.Battle;
using FunProject.Combatants;
using GdUnit4;
using Godot;
using System;

[TestSuite]
[RequireGodotRuntime]
public class BattleResultTest
{
  [TestCase(TestName = "The completion query answers None for a running battle")]
  public void RejectsInProgress()
  {
    var player = TestData.MakeFaction("P");
    var enemy = TestData.MakeFaction("E");
    using var battle = BattleFixture.Started(new Vector3I(4, 1, 4),
      new UnitPlacement(new UnitLoadout(TestData.MakeCombatant("A", player)), new Vector3I(0, 0, 0)),
      new UnitPlacement(new UnitLoadout(TestData.MakeCombatant("B", enemy)), new Vector3I(3, 0, 3)));

    Assert.True(battle.Query(new GetCompletedBattleQuery()).IsNone);
    Assert.True(battle.Query(new GetCurrentTurnQuery()).IsSome);
  }

  [TestCase(TestName = "The frozen completion reports outcome, counts, and object tallies")]
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
    runtime.ExecuteAction(BattleAction.EndFactionTurn(
      runtime.Query(new GetCurrentTurnQuery()).RequireSome().ActiveFaction));

    CompletedBattle completed = runtime.Query(new GetCompletedBattleQuery()).RequireSome();
    Assert.Equal(BattleOutcome.Defeat, completed.Outcome);
    Assert.Equal(1, completed.TurnCount);
    Assert.Equal(2, completed.Factions.Count);
    Assert.Equal(1, completed.ObjectsExpired);
    Assert.Equal(0, completed.ObjectsInteracted);
    Assert.Equal(1, completed.Factions[player].Spawned);
    Assert.Equal(0, completed.Factions[player].Killed);
    Assert.Equal(1, completed.Factions[enemy].Spawned);
    Assert.Equal(0, completed.Factions[enemy].Killed);

    // Repeated reads serve the same stored report.
    Assert.True(ReferenceEquals(completed, runtime.Query(new GetCompletedBattleQuery()).RequireSome()));
  }
}
