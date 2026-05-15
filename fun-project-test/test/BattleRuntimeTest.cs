using FunProject.Battle;
using FunProject.Tests;
using GdUnit4;
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using static BattleActionTestHelper;
using static BattleQueryTestHelper;

[TestSuite]
[RequireGodotRuntime]
public sealed partial class BattleRuntimeTest
{
  [TestCase(TestName = "Constructor throws when session is null")]
  public void ConstructorThrowsWhenSessionIsNull()
  {
    Assert.Throws<ArgumentNullException>(() => new BattleRuntime(null));
  }

  [TestCase(TestName = "Query returns query runner result")]
  public void QueryReturnsQueryRunnerResult()
  {
    var faction = BattleTestFactory.MakeFaction("Player");
    var session = BattleTestFactory.MakeSession(new Vector3I(3, 1, 3), [faction]);
    var foreignSession = BattleTestFactory.MakeSession(new Vector3I(3, 1, 3), [faction]);
    var foreignUnit = SpawnUnit(foreignSession, BattleTestFactory.MakeCombatant("Foreign", faction), new Vector3I(0, 0, 0));
    var runtime = new BattleRuntime(session);

    Either<BattleQueryFailure, BattleUnitState> result = runtime.Query(new GetLivingUnit(foreignUnit.Handle));

    Assert.True(result.IsLeft);
    BattleQueryFailure failure = GetFailure(result);
    Assert.Equal(BattleQueryFailureReason.UnknownUnit, failure.Reason);
  }

  [TestCase(TestName = "BattleSession does not expose Queries as public property")]
  public void BattleSessionDoesNotExposeQueriesAsPublicProperty()
  {
    var property = typeof(BattleSession).GetProperty("Queries");

    Assert.True(property == null || property.GetMethod == null || !property.GetMethod.IsPublic);
  }

  [TestCase(TestName = "ExecuteAction delegates to action executor")]
  public void ExecuteActionDelegatesToActionExecutor()
  {
    var faction = BattleTestFactory.MakeFaction("Player");
    var session = BattleTestFactory.MakeSession(new Vector3I(3, 1, 3), [faction]);
    var runtime = new BattleRuntime(session);

    BattleActionResult result = runtime
      .ExecuteAction(BattleAction.SpawnUnit(BattleTestFactory.MakeCombatant("Alpha", faction), new Vector3I(1, 0, 1)))
      .RequireSingleResult();

    Assert.True(result.Succeeded);
    BattleSession.BattleUnitHandle handle = result.AffectedUnitHandle.RequireSome();
    Assert.True(session.GetUnit(handle).IsSome);
    Assert.Equal(0, runtime.PendingActionCount);
    Assert.Equal(result, runtime.LastActionResult.RequireSome());
  }

  [TestCase(TestName = "Runtime republishes session and action events")]
  public void RuntimeRepublishesSessionAndActionEvents()
  {
    var faction = BattleTestFactory.MakeFaction("Player");
    var session = BattleTestFactory.MakeSession(new Vector3I(3, 1, 3), [faction]);
    var runtime = new BattleRuntime(session);
    var committedEvents = new List<BattleEvent>();
    var startedActions = new List<BattleAction>();
    var completedResults = new List<BattleActionResult>();
    runtime.BattleEventCommitted += committedEvents.Add;
    runtime.ActionStarted += startedActions.Add;
    runtime.ActionCompleted += completedResults.Add;

    BattleAction action = BattleAction.SpawnUnit(
      BattleTestFactory.MakeCombatant("Alpha", faction),
      new Vector3I(1, 0, 1));

    BattleActionResult result = runtime.ExecuteAction(action).RequireSingleResult();

    Assert.True(result.Succeeded);
    Assert.Equal(1, startedActions.Count);
    Assert.True(object.ReferenceEquals(action, startedActions[0]));
    Assert.Equal(1, completedResults.Count);
    Assert.Equal(result, completedResults[0]);
    Assert.True(committedEvents.OfType<UnitAddedBattleEvent>().Any());
  }

  [TestCase(TestName = "RegisterTrigger affects runtime action execution")]
  public void RegisterTriggerAffectsRuntimeActionExecution()
  {
    var faction = BattleTestFactory.MakeFaction("Player");
    var session = BattleTestFactory.MakeSession(new Vector3I(3, 1, 3), [faction]);
    var unit = SpawnUnit(session, BattleTestFactory.MakeCombatant("Alpha", faction, actionPoints: 5), new Vector3I(0, 0, 0));
    StartBattle(session);
    var runtime = new BattleRuntime(session);
    var log = new List<string>();
    var targetPosition = new Vector3I(1, 0, 0);
    runtime.RegisterTrigger(new RuntimeRecordingTrigger("runtime_trigger", targetPosition, log), BattleEventType.UnitMoved);

    BattleActionResult result = runtime
      .ExecuteAction(BattleAction.MoveUnit(unit.Handle, [targetPosition]))
      .RequireSingleResult();

    Assert.True(result.Succeeded);
    Assert.Equal(1, log.Count);
    Assert.Equal("runtime_trigger", log[0]);
  }

  [TestCase(TestName = "RegisterTrigger collection overload affects runtime action execution")]
  public void RegisterTriggerCollectionOverloadAffectsRuntimeActionExecution()
  {
    var faction = BattleTestFactory.MakeFaction("Player");
    var session = BattleTestFactory.MakeSession(new Vector3I(3, 1, 3), [faction]);
    var unit = SpawnUnit(session, BattleTestFactory.MakeCombatant("Alpha", faction, actionPoints: 5), new Vector3I(0, 0, 0));
    StartBattle(session);
    var runtime = new BattleRuntime(session);
    var log = new List<string>();
    var targetPosition = new Vector3I(1, 0, 0);
    runtime.RegisterTrigger(
      new RuntimeRecordingTrigger("runtime_collection_trigger", targetPosition, log),
      new[] { BattleEventType.UnitMoved });

    BattleActionResult result = runtime
      .ExecuteAction(BattleAction.MoveUnit(unit.Handle, [targetPosition]))
      .RequireSingleResult();

    Assert.True(result.Succeeded);
    Assert.Equal(1, log.Count);
    Assert.Equal("runtime_collection_trigger", log[0]);
  }

  [TestCase(TestName = "Disposed runtime no longer republishes session events")]
  public void DisposedRuntimeNoLongerRepublishesSessionEvents()
  {
    var faction = BattleTestFactory.MakeFaction("Player");
    var session = BattleTestFactory.MakeSession(new Vector3I(3, 1, 3), [faction]);
    var runtime = new BattleRuntime(session);
    var committedEvents = new List<BattleEvent>();
    runtime.BattleEventCommitted += committedEvents.Add;

    runtime.Dispose();
    new BattleActionExecutor(session)
      .Submit(BattleAction.SpawnUnit(BattleTestFactory.MakeCombatant("Alpha", faction), new Vector3I(1, 0, 1)))
      .RequireSingleResult();

    Assert.Equal(0, committedEvents.Count);
  }

  [TestCase(TestName = "Public methods throw after runtime is disposed")]
  public void PublicMethodsThrowAfterRuntimeIsDisposed()
  {
    var faction = BattleTestFactory.MakeFaction("Player");
    var session = BattleTestFactory.MakeSession(new Vector3I(3, 1, 3), [faction]);
    var runtime = new BattleRuntime(session);

    runtime.Dispose();

    Assert.Throws<ObjectDisposedException>(() => _ = runtime.PendingActionCount);
    Assert.Throws<ObjectDisposedException>(() => _ = runtime.LastActionResult);
    Assert.Throws<ObjectDisposedException>(() => runtime.Query(new GetFactionAliveUnits(faction)));
    Assert.Throws<ObjectDisposedException>(() => runtime.ExecuteAction(BattleAction.SpawnUnit(
      BattleTestFactory.MakeCombatant("Alpha", faction),
      new Vector3I(1, 0, 1))));
    Assert.Throws<ObjectDisposedException>(() => runtime.RegisterTrigger(
      new RuntimeRecordingTrigger("runtime_trigger", new Vector3I(1, 0, 0), []),
      BattleEventType.UnitMoved));
    Assert.Throws<ObjectDisposedException>(() => runtime.RegisterTrigger(
      new RuntimeRecordingTrigger("runtime_collection_trigger", new Vector3I(1, 0, 0), []),
      new[] { BattleEventType.UnitMoved }));
  }

  [TestCase(TestName = "Dispose is idempotent")]
  public void DisposeIsIdempotent()
  {
    var faction = BattleTestFactory.MakeFaction("Player");
    var session = BattleTestFactory.MakeSession(new Vector3I(3, 1, 3), [faction]);
    var runtime = new BattleRuntime(session);

    runtime.Dispose();
    runtime.Dispose();
  }

  private sealed partial class RuntimeRecordingTrigger : BattleTrigger
  {
    private readonly Vector3I _targetPosition;
    private readonly List<string> _log;

    public RuntimeRecordingTrigger(string triggerId, Vector3I targetPosition, List<string> log)
      : base(triggerId)
    {
      _targetPosition = targetPosition;
      _log = log;
    }

    public override bool Matches(BattleEvent battleEvent)
    {
      return battleEvent is UnitMovedBattleEvent movedEvent
        && movedEvent.Position.Raw == _targetPosition;
    }

    public override BattleTriggerResult Evaluate(BattleSession session, BattleEvent battleEvent, BattleAction sourceAction)
    {
      _log.Add(TriggerId);
      return BattleTriggerResult.NoReaction();
    }
  }
}
