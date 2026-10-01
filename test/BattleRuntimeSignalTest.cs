using FunProject.Battle;
using FunProject.Weapons;
using GdUnit4;
using Godot;
using System.Collections.Generic;

[TestSuite]
[RequireGodotRuntime]
public sealed partial class BattleRuntimeSignalTest
{
  [TestCase(TestName = "ExecuteAction raises ActionStarted then ActionCompleted with events attached")]
  public void ExecuteActionRaisesLifecycleSignalsInOrder()
  {
    using var battle = BattleFixture.Solo(new Vector3I(3, 1, 3), new Vector3I(0, 0, 0), actionPoints: 5);
    var runtime = battle.Runtime;

    List<string> order = [];
    List<BattleActionExecResult> completed = [];
    runtime.ActionStarted += _ => order.Add("started");
    runtime.ActionCompleted += result =>
    {
      order.Add("completed");
      completed.Add(result);
    };

    runtime.ExecuteAction(BattleAction.MoveUnit(
      battle.Alive(battle.Unit), [battle.At(1, 0, 1)])).RequireSome();

    Assert.Equal(2, order.Count);
    Assert.Equal("started", order[0]);
    Assert.Equal("completed", order[1]);
    Assert.Equal(1, completed.Count);
    bool movementCommitted = false;
    foreach (BattleEvent battleEvent in completed[0].EventsThatOccurred)
      movementCommitted |= battleEvent is UnitMovedBattleEvent;
    Assert.True(movementCommitted);
  }

  [TestCase(TestName = "Completed never fires when Submit throws; Started marks the attempt")]
  public void NoCompletedSignalWhenSubmitThrows()
  {
    using var battle = BattleFixture.Solo(new Vector3I(3, 1, 3), new Vector3I(0, 0, 0), actionPoints: 5);
    var runtime = battle.Runtime;
    int started = 0;
    int completed = 0;
    runtime.ActionStarted += _ => started++;
    runtime.ActionCompleted += _ => completed++;

    runtime.ExecuteAction(BattleAction.MoveUnit(
      battle.Alive(battle.Unit), [battle.At(1, 0, 1)])).RequireSome();
    // The disconnected route rejects: an invariant-break throw out of the submission.
    Assert.Throws<System.InvalidOperationException>(() => runtime.ExecuteAction(BattleAction.MoveUnit(
      battle.Alive(battle.Unit), [battle.At(0, 0, 0), battle.At(2, 0, 2)])).RequireSome());

    // Started marks the attempt (fires before Submit); Completed is the reaction signal and
    // must never fire for a failed submission — spec: "no reaction runs".
    Assert.Equal(2, started);
    Assert.Equal(1, completed);
  }

  [TestCase(TestName = "A submission against a completed runtime returns None with no lifecycle signals")]
  public void CompletedRuntimeSubmissionsReturnNone()
  {
    using var battle = BattleFixture.Duel(playerControlled: true);
    battle.ApplyDamage(battle.EnemyUnit, 20, DamageKind.Stun);
    var runtime = battle.Runtime;
    int started = 0;
    int completed = 0;
    runtime.ActionStarted += _ => started++;
    runtime.ActionCompleted += _ => completed++;
    battle.ClearEvents();
    Vector3I positionBefore = battle.PositionOf(battle.PlayerUnit).RequireSome().Raw;
    int apBefore = battle.PlayerUnit.CurrentActionPoints;

    Assert.True(battle.Submit(BattleAction.EndFactionTurn(battle.PlayerFaction)).IsNone);

    Assert.Equal(0, started);
    Assert.Equal(0, completed);
    Assert.Equal(0, battle.Events.Count);
    // A fresh completed submission mutates nothing: AP and position stay frozen too.
    Assert.Equal(positionBefore, battle.PositionOf(battle.PlayerUnit).RequireSome().Raw);
    Assert.Equal(apBefore, battle.PlayerUnit.CurrentActionPoints);
    Assert.True(battle.Query(new GetCurrentTurnQuery()).IsNone);
    Assert.True(battle.Query(new GetCompletedBattleQuery()).IsSome);
  }
}
