using FunProject.Battle;
using FunProject.Weapons;
using GdUnit4;
using Godot;
using System;
using System.Collections.Generic;

[TestSuite]
[RequireGodotRuntime]
public sealed partial class BattleRuntimeSignalTest
{
  [TestCase(TestName = "ExecuteAction raises ActionStarted then ActionCompleted with events attached")]
  public void ExecuteActionRaisesLifecycleSignalsInOrder()
  {
    using var battle = BattleFixture.Solo(new Vector3I(3, 1, 3), new Vector3I(0, 0, 0), actionPoints: 5);
    var unit = battle.Unit;
    var runtime = battle.Runtime;

    List<string> order = [];
    List<BattleActionExecResult> completed = [];
    int nestedAttempts = 0;
    runtime.ActionStarted += _ =>
    {
      order.Add("started");
      // The window's early edge: a nested submission is rejected before its damage cost
      // lands, and the outer submission continues intact.
      if (nestedAttempts++ == 0)
      {
        Assert.Throws<InvalidOperationException>(() => runtime.ExecuteAction(BattleAction.ApplyDamage(
          runtime.TryGetAlive(unit).RequireSome(), 1)));
        Assert.Equal(20, unit.CurrentHealth);
      }
    };
    runtime.ActionCompleted += result =>
    {
      // The first result is recorded BEFORE the adjacent submission, so this callback never
      // re-enters its own window; the second completion just closes the later submission.
      order.Add("completed");
      completed.Add(result);
      if (completed.Count == 1)
        runtime.ExecuteAction(BattleAction.MoveUnit(battle.Alive(unit), [battle.At(2, 0, 2)]));
    };

    BattleActionExecResult first = runtime.ExecuteAction(
      BattleAction.MoveUnit(battle.Alive(unit), [battle.At(1, 0, 1)])).RequireSome();

    // Exactly one lifecycle pair per submission: the rejected nested attempt never opened
    // one; the adjacent callback submission did.
    Assert.True(order.AsValueEnumerable().SequenceEqual(["started", "completed", "started", "completed"]));
    Assert.Equal(2, completed.Count);
    // The closed window is bounded: the first result holds only its own movement and tile
    // pair, while the later submission moved the unit a second time separately.
    BattleEvent[] firstEvents = first.EventsThatOccurred.ToArray();
    Assert.Equal(2, firstEvents.Length);
    Assert.Equal(new Vector3I(1, 0, 1), firstEvents.SingleEvent<UnitMovedBattleEvent>().Position.Raw);
    Assert.Equal(new Vector3I(2, 0, 2), battle.PositionOf(unit).RequireSome().Raw);
    Assert.Equal(20, unit.CurrentHealth);
  }

  [TestCase(TestName = "Completed never fires when Submit throws; Started marks the attempt")]
  public void NoCompletedSignalWhenSubmitThrows()
  {
    using var battle = BattleFixture.Solo(new Vector3I(3, 1, 3), new Vector3I(0, 0, 0), actionPoints: 5);
    var unit = battle.Unit;
    var runtime = battle.Runtime;
    int started = 0;
    int completed = 0;
    runtime.ActionStarted += _ => started++;
    runtime.ActionCompleted += _ => completed++;

    runtime.ExecuteAction(BattleAction.MoveUnit(
      battle.Alive(unit), [battle.At(1, 0, 1)])).RequireSome();

    // A one-shot ActionStarted sentinel faults a valid route in-flight: the observer failure
    // releases the window — exact cause identity, no movement, no completion.
    var expected = new InvalidOperationException("started observer fault");
    bool thrown = false;
    runtime.ActionStarted += _ =>
    {
      if (!thrown)
      {
        thrown = true;
        throw expected;
      }
    };

    Exception? caught = null;
    try
    {
      runtime.ExecuteAction(BattleAction.MoveUnit(battle.Alive(unit), [battle.At(1, 0, 0)]));
    }
    catch (Exception error)
    {
      caught = error;
    }

    Assert.True(ReferenceEquals(expected, caught));
    // Only the opening move completed; the faulted attempt fired Started but never Completed.
    Assert.Equal(1, completed);
    Assert.Equal(new Vector3I(1, 0, 1), battle.PositionOf(unit).RequireSome().Raw);

    // The sentinel disarmed itself; a fresh valid submission recovers and completes.
    Assert.True(runtime.ExecuteAction(
      BattleAction.MoveUnit(battle.Alive(unit), [battle.At(1, 0, 0)])).IsSome);
    Assert.Equal(new Vector3I(1, 0, 0), battle.PositionOf(unit).RequireSome().Raw);
    // Started marks every attempt (including the faulted one); Completed marks only successes
    // (the opening move and the recovery — never the faulted attempt).
    Assert.Equal(3, started);
    Assert.Equal(2, completed);
  }

  [TestCase(TestName = "An ActionCompleted observer fault is a notification failure, not a failed submission")]
  public void ActionCompletedObserverFaultIsNotificationFailureOnly()
  {
    using var battle = BattleFixture.Solo(new Vector3I(5, 1, 5), new Vector3I(0, 0, 0), actionPoints: 5);
    var unit = battle.Unit;
    var runtime = battle.Runtime;
    var expected = new InvalidOperationException("completed observer fault");
    bool thrown = false;
    runtime.ActionCompleted += _ =>
    {
      if (!thrown)
      {
        thrown = true;
        throw expected;
      }
    };

    Exception? caught = null;
    try
    {
      runtime.ExecuteAction(BattleAction.MoveUnit(battle.Alive(unit), [battle.At(1, 0, 0)]));
    }
    catch (Exception error)
    {
      caught = error;
    }

    // The notification failure propagates, but the primitive's committed state stands.
    Assert.True(ReferenceEquals(expected, caught));
    Assert.Equal(new Vector3I(1, 0, 0), battle.PositionOf(unit).RequireSome().Raw);

    // A later submission still completes normally; the earlier fault did not poison it.
    battle.ApplyDamage(unit, 1);
    Assert.Equal(19, unit.CurrentHealth);
  }
}
