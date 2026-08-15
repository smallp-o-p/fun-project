using FunProject.Battle;
using GdUnit4;
using Godot;
using System.Collections.Generic;
using System.Linq;

[TestSuite]
[RequireGodotRuntime]
public sealed partial class BattleRuntimeSignalTest
{
  [TestCase(TestName = "ExecuteAction raises ActionStarted then ActionCompleted with events attached")]
  public void ExecuteActionRaisesLifecycleSignalsInOrder()
  {
    var faction = BattleTestFactory.MakeFaction("Player");
    var session = BattleTestFactory.MakeSession(new Vector3I(3, 1, 3), [faction]);
    using var runtime = new BattleRuntime(session);

    List<string> order = [];
    List<BattleActionExecResult> completed = [];
    runtime.ActionStarted += _ => order.Add("started");
    runtime.ActionCompleted += result =>
    {
      order.Add("completed");
      completed.Add(result);
    };

    runtime.ExecuteAction(BattleAction.SpawnUnit(
      BattleTestFactory.MakeCombatant("Alpha", faction),
      session.Board.At(1, 0, 1)));

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
  public void NoCompletedSignalWhenSubmitThrows()
  {
    var faction = BattleTestFactory.MakeFaction("Player");
    var session = BattleTestFactory.MakeSession(new Vector3I(3, 1, 3), [faction]);
    using var runtime = new BattleRuntime(session);
    int started = 0;
    int completed = 0;
    runtime.ActionStarted += _ => started++;
    runtime.ActionCompleted += _ => completed++;

    runtime.ExecuteAction(BattleAction.SpawnUnit(
      BattleTestFactory.MakeCombatant("A", faction), session.Board.At(1, 0, 1)));
    Assert.Throws<System.InvalidOperationException>(() => runtime.ExecuteAction(BattleAction.SpawnUnit(
      BattleTestFactory.MakeCombatant("B", faction), session.Board.At(1, 0, 1))));

    // Started marks the attempt (fires before Submit); Completed is the reaction signal and
    // must never fire for a failed submission — spec: "no reaction runs".
    Assert.Equal(2, started);
    Assert.Equal(1, completed);
  }
}
