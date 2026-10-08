using FunProject.Battle;
using GdUnit4;
using Godot;
using System;
using System.Collections.Generic;

namespace FunProject.Tests;

[TestSuite]
[RequireGodotRuntime]
public sealed partial class BattleRuntimeSignalTest
{
  [TestCase(TestName = "ExecuteAction raises ActionStarted then ActionCompleted with events attached")]
  public static void ExecuteActionRaisesLifecycleSignalsInOrder()
  {
    using var battle = BattleFixture.Solo(new Vector3I(3, 1, 3), new Vector3I(0, 0, 0));
    var runtime = battle.Runtime;

    List<string> order = [];
    List<BattleActionExecResult> completed = [];
    runtime.ActionStarted += _ => order.Add("started");
    runtime.ActionCompleted += result =>
    {
      order.Add("completed");
      completed.Add(result);
    };

    // The same spawn operation as any other action, as an ordinary reinforcement.
    runtime.ExecuteAction(BattleAction.SpawnUnit(
      TestData.MakeCombatant("Reinforcement", battle.PlayerFaction),
      battle.Board.At(1, 0, 1)));

    Assert.Equal(2, order.Count);
    Assert.Equal("started", order[0]);
    Assert.Equal("completed", order[1]);
    Assert.Equal(1, completed.Count);
    bool unitAdded = false;
    foreach (BattleEvent battleEvent in completed[0].EventsThatOccurred)
      unitAdded |= battleEvent is UnitAddedBattleEvent;
    Assert.True(unitAdded);
  }

  [TestCase(TestName = "Completed never fires when Submit throws; Started marks the attempt")]
  public static void NoCompletedSignalWhenSubmitThrows()
  {
    using var battle = BattleFixture.Solo(new Vector3I(3, 1, 3), new Vector3I(0, 0, 0));
    var runtime = battle.Runtime;
    int started = 0;
    int completed = 0;
    runtime.ActionStarted += _ => started++;
    runtime.ActionCompleted += _ => completed++;

    runtime.ExecuteAction(BattleAction.SpawnUnit(
      TestData.MakeCombatant("Reinforcement", battle.PlayerFaction), battle.Board.At(1, 0, 1)));
    Assert.Throws<System.InvalidOperationException>(() => runtime.ExecuteAction(BattleAction.SpawnUnit(
      TestData.MakeCombatant("Second", battle.PlayerFaction), battle.Board.At(1, 0, 1))));

    // Started marks the attempt (fires before Submit); Completed is the reaction signal and
    // must never fire for a failed submission — spec: "no reaction runs".
    Assert.Equal(2, started);
    Assert.Equal(1, completed);
  }

  [TestCase(TestName = "A completed battle rejects a new submission before either action signal fires")]
  public static void CompletedBattleRejectsSubmissionBeforeActionSignals()
  {
    using var battle = BattleFixture.Duel(playerControlled: true);
    battle.ApplyDamage(battle.EnemyUnit, 999); // the player's eliminate-all objective ends the battle
    battle.ClearEvents();
    int started = 0;
    int completed = 0;
    battle.Runtime.ActionStarted += _ => started++;
    battle.Runtime.ActionCompleted += _ => completed++;
    var action = BattleAction.SpawnUnit(
      TestData.MakeCombatant("Reinforcement", battle.PlayerFaction), battle.At(0, 0, 0));

    Assert.Throws<InvalidOperationException>(() => battle.Submit(action));

    Assert.Equal(0, started);
    Assert.Equal(0, completed);
    Assert.Equal(0, battle.Events.Count);
    Assert.True(battle.Query(new GetUnitAtTile(battle.At(0, 0, 0))).IsNone);
  }
}
