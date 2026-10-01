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

  public enum NestedAttemptSource
  {
    ActionStarted,
    Primitive,
    HookDispatch,
  }

  [TestCase(NestedAttemptSource.ActionStarted, TestName = "A nested submission from ActionStarted is rejected and the outer submission stays intact")]
  [TestCase(NestedAttemptSource.Primitive, TestName = "A nested submission from a custom primitive is rejected and the outer submission stays intact")]
  [TestCase(NestedAttemptSource.HookDispatch, TestName = "A nested submission from hook dispatch is rejected and the outer submission stays intact")]
  public void NestedSubmissionsAreRejectedAndOuterSubmissionStaysIntact(NestedAttemptSource source)
  {
    using var battle = BattleFixture.Solo(new Vector3I(4, 1, 4), new Vector3I(0, 0, 0), actionPoints: 5);
    var unit = battle.Unit;
    var runtime = battle.Runtime;
    bool rejected = false;
    int startedCount = 0;
    int completedCount = 0;
    runtime.ActionStarted += _ => startedCount++;
    runtime.ActionCompleted += _ => completedCount++;
    BattleActionExecResult outerResult;

    switch (source)
    {
      case NestedAttemptSource.ActionStarted:
        {
          bool attempted = false;
          runtime.ActionStarted += _ =>
          {
            if (attempted)
              return;
            attempted = true;
            rejected = TryNested(runtime, unit);
          };
          outerResult = runtime.ExecuteAction(
            BattleAction.MoveUnit(battle.Alive(unit), [battle.At(1, 0, 0)])).RequireSome();
          Assert.True(rejected);
          Assert.Equal(new Vector3I(1, 0, 0), battle.PositionOf(unit).RequireSome().Raw);
          Assert.Equal(20, unit.CurrentHealth);
          AssertMoveBeforeTileOccupied(outerResult);
          // Exactly one lifecycle pair fired: the rejected nested attempt never opened one.
          Assert.Equal(1, startedCount);
          Assert.Equal(1, completedCount);
          break;
        }
      case NestedAttemptSource.Primitive:
        {
          var primitive = new NestedSubmitPrimitive(runtime, unit);
          outerResult = runtime.ExecuteAction(primitive).RequireSome();
          Assert.True(primitive.Rejected);
          Assert.Equal(new Vector3I(0, 0, 0), battle.PositionOf(unit).RequireSome().Raw);
          Assert.Equal(20, unit.CurrentHealth);
          // Exactly one lifecycle pair fired: the rejected nested attempt never opened one.
          Assert.Equal(1, startedCount);
          Assert.Equal(1, completedCount);
          // The engine took no stale work from the rejected attempt: a fresh submission runs.
          runtime.ExecuteAction(BattleAction.MoveUnit(battle.Alive(unit), [battle.At(1, 0, 0)])).RequireSome();
          Assert.Equal(new Vector3I(1, 0, 0), battle.PositionOf(unit).RequireSome().Raw);
          break;
        }
      default:
        {
          var hook = new NestedSubmitHook(runtime, unit);
          battle.RegisterHook<UnitMovedBattleEvent>(hook);
          outerResult = runtime.ExecuteAction(
            BattleAction.MoveUnit(battle.Alive(unit), [battle.At(1, 0, 0)])).RequireSome();
          Assert.True(hook.Rejected);
          Assert.Equal(new Vector3I(1, 0, 0), battle.PositionOf(unit).RequireSome().Raw);
          Assert.Equal(20, unit.CurrentHealth);
          AssertMoveBeforeTileOccupied(outerResult);
          // Exactly one lifecycle pair fired: the rejected nested attempt never opened one.
          Assert.Equal(1, startedCount);
          Assert.Equal(1, completedCount);
          break;
        }
    }
  }

  private static void AssertMoveBeforeTileOccupied(BattleActionExecResult result)
  {
    BattleEvent[] outerEvents = result.EventsThatOccurred.ToArray();
    Assert.Equal(1, outerEvents.EventsOf<UnitMovedBattleEvent>().Length);
    Assert.Equal(1, outerEvents.EventsOf<TileOccupiedBattleEvent>().Length);
    Assert.True(outerEvents.EventIndex<UnitMovedBattleEvent>()
      < outerEvents.EventIndex<TileOccupiedBattleEvent>());
  }

  // Attempts a nested submission from inside the outer window; the damage cost is the
  // observable that proves the attempt never executed.
  private static bool TryNested(BattleRuntime runtime, BattleUnitState unit)
  {
    try
    {
      runtime.ExecuteAction(BattleAction.ApplyDamage(
        runtime.TryGetAlive(unit).RequireSome(), 1));
      return false;
    }
    catch (InvalidOperationException)
    {
      return true;
    }
  }

  [TestCase(TestName = "A fault thrown from ActionStarted fails the submission and the next one recovers")]
  public void ThrowingActionStartedObserverFailsSubmissionAndRecovers()
  {
    using var battle = BattleFixture.Solo(new Vector3I(3, 1, 3), new Vector3I(0, 0, 0), actionPoints: 5);
    var unit = battle.Unit;
    var runtime = battle.Runtime;
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
    int completedCount = 0;
    runtime.ActionCompleted += _ => completedCount++;

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
    Assert.Equal(0, completedCount);
    Assert.Equal(new Vector3I(0, 0, 0), battle.PositionOf(unit).RequireSome().Raw);

    Assert.True(runtime.ExecuteAction(
      BattleAction.MoveUnit(battle.Alive(unit), [battle.At(1, 0, 0)])).IsSome);
    Assert.Equal(new Vector3I(1, 0, 0), battle.PositionOf(unit).RequireSome().Raw);
  }

  [TestCase(TestName = "An ActionCompleted observer may submit a separate action; the original result excludes the later events")]
  public void ActionCompletedObserverMaySubmitAfterWindowCloses()
  {
    using var battle = BattleFixture.Solo(new Vector3I(5, 1, 5), new Vector3I(0, 0, 0), actionPoints: 5);
    var unit = battle.Unit;
    var runtime = battle.Runtime;
    BattleAction? observerSeenAction = null;
    runtime.ActionCompleted += result =>
    {
      if (observerSeenAction is not null)
        return;
      observerSeenAction = result.Action;
      runtime.ExecuteAction(BattleAction.MoveUnit(battle.Alive(unit), [battle.At(2, 0, 0)]));
    };

    BattleActionExecResult original = runtime.ExecuteAction(
      BattleAction.MoveUnit(battle.Alive(unit), [battle.At(1, 0, 0)])).RequireSome();

    Assert.True(ReferenceEquals(original.Action, observerSeenAction));
    // The bounded window holds only the first submission's own movement events.
    Assert.Equal(2, original.EventsThatOccurred.Length);
    Assert.Equal(new Vector3I(1, 0, 0),
      original.EventsThatOccurred.ToArray().SingleEvent<UnitMovedBattleEvent>().Position.Raw);
    // The later submission ran separately and moved the unit again.
    Assert.Equal(new Vector3I(2, 0, 0), battle.PositionOf(unit).RequireSome().Raw);
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

  // The outer submission itself: completes after its rejected nested attempt.
  private sealed partial class NestedSubmitPrimitive(BattleRuntime runtime, BattleUnitState unit) : BattleAction
  {
    public bool Rejected { get; private set; }

    internal override Result ExecuteStep(BattleSession session)
    {
      Rejected = TryNested(runtime, unit);
      return Result.Completed;
    }
  }

  private sealed class NestedSubmitHook(BattleRuntime runtime, BattleUnitState unit) : BattleHook<UnitMovedBattleEvent>
  {
    public bool Rejected { get; private set; }

    protected override IReadOnlyList<BattleAction> OnEvent(HookContext context, UnitMovedBattleEvent evt)
    {
      Rejected = TryNested(runtime, unit);
      return [];
    }
  }
}
