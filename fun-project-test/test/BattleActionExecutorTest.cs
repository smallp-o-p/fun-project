using FunProject.Battle;
using FunProject.Tests;
using GdUnit4;
using Godot;
using static BattleActionTestHelper;
using static BattleQueryTestHelper;

[TestSuite]
[RequireGodotRuntime]
public partial class BattleActionExecutorTest
{
  [TestCase(TestName = "Submit commits source action and reaction actions")]
  public void SubmitCommitsSourceActionAndReactionActions()
  {
    var faction = BattleTestFactory.MakeFaction("Player");
    var session = BattleTestFactory.MakeSession(new Vector3I(3, 1, 3), [faction]);
    var unit = SpawnUnit(session, BattleTestFactory.MakeCombatant("Alpha", faction, health: 10, actionPoints: 5), new Vector3I(0, 0, 0));
    StartBattle(session);

    var executor = new BattleActionExecutor(session);
    var targetPosition = new Vector3I(1, 0, 0);
    executor.RegisterTrigger(new DamageOnTileOccupiedTrigger("mine", targetPosition, unit.Handle, 3, shouldConsume: true), BattleEventType.TileOccupied);
    var resolvedActions = new List<BattleAction>();
    executor.OnActionComplete += result => resolvedActions.Add(result.Action);

    BattleAction submittedAction = BattleAction.MoveUnitStep(unit.Handle, targetPosition);
    IReadOnlyList<BattleActionResult> results = executor.Submit(submittedAction);

    Assert.Equal(2, results.Count);
    Assert.Equal(submittedAction, results[0].Action);
    Assert.True(results[0].Succeeded);
    Assert.True(results[1].Succeeded);
    Assert.True(results[1].Action is ApplyDamage);
    Assert.Equal(targetPosition, session.GetUnitPosition(unit.Handle).RequireSome().Raw);
    Assert.Equal(7, unit.State.CurrentHealth);
    Assert.Equal(0, executor.PendingCount);
    Assert.True(resolvedActions.Select(action => action.ActionId).SequenceEqual([
      MoveUnitStep.MoveUnitStepActionId,
      ApplyDamage.ApplyDamageActionId,
    ]));
  }

  [TestCase(TestName = "Submit commits composite primitive actions and reactions")]
  public void SubmitCommitsCompositePrimitiveActionsAndReactions()
  {
    var faction = BattleTestFactory.MakeFaction("Player");
    var session = BattleTestFactory.MakeSession(new Vector3I(4, 1, 4), [faction]);
    var start = new Vector3I(0, 0, 0);
    var mid = new Vector3I(1, 0, 0);
    var end = new Vector3I(2, 0, 0);
    var unit = SpawnUnit(session, BattleTestFactory.MakeCombatant("Alpha", faction, health: 10, actionPoints: 5), start);
    StartBattle(session);

    var executor = new BattleActionExecutor(session);
    executor.RegisterTrigger(new DamageOnTileOccupiedTrigger("mine", mid, unit.Handle, 3, shouldConsume: true), BattleEventType.TileOccupied);
    var resolvedActions = new List<BattleAction>();
    executor.OnActionComplete += result => resolvedActions.Add(result.Action);

    BattleAction submittedAction = new MoveUnit(unit.Handle, [start, mid, end]);
    IReadOnlyList<BattleActionResult> results = executor.Submit(submittedAction);

    Assert.Equal(3, results.Count);
    Assert.True(results.All(result => result.Succeeded));
    Assert.True(results[0].Action is MoveUnitStep);
    Assert.True(results[1].Action is ApplyDamage);
    Assert.True(results[2].Action is MoveUnitStep);
    Assert.Equal(end, session.GetUnitPosition(unit.Handle).RequireSome().Raw);
    Assert.Equal(7, unit.State.CurrentHealth);
    Assert.Equal(0, executor.PendingCount);
    Assert.True(submittedAction.IsDone());
    Assert.True(resolvedActions.Select(action => action.ActionId).SequenceEqual([
      MoveUnitStep.MoveUnitStepActionId,
      ApplyDamage.ApplyDamageActionId,
      MoveUnitStep.MoveUnitStepActionId,
    ]));
  }

  [TestCase(TestName = "MoveUnitStep emits movement and tile occupation events after commit")]
  public void MoveUnitStepEmitsMovementAndTileOccupationEventsAfterCommit()
  {
    var faction = BattleTestFactory.MakeFaction("Player");
    var session = BattleTestFactory.MakeSession(new Vector3I(4, 1, 4), [faction]);
    var unit = SpawnUnit(session, BattleTestFactory.MakeCombatant("Alpha", faction, actionPoints: 5), new Vector3I(1, 0, 1));
    StartBattle(session);

    var raisedEvents = new List<BattleEvent>();
    session.BattleEventCommitted += raisedEvents.Add;

    var executor = new BattleActionExecutor(session);
    var result = executor.Submit(BattleAction.MoveUnitStep(unit.Handle, new Vector3I(1, 0, 2), 2));

    Assert.True(result.First().Succeeded);
    Assert.True(raisedEvents.OfType<UnitMovedBattleEvent>().Any(battleEvent =>
      battleEvent.UnitId == unit.UnitId &&
      battleEvent.Position.Raw == new Vector3I(1, 0, 2) &&
      battleEvent.SourcePosition.Raw == new Vector3I(1, 0, 1)));
    Assert.True(raisedEvents.OfType<TileOccupiedBattleEvent>().Any(battleEvent =>
      battleEvent.UnitId == unit.UnitId &&
      battleEvent.Position.Raw == new Vector3I(1, 0, 2)));
  }

  [TestCase(TestName = "Committed battle event listeners observe committed session state")]
  public void CommittedBattleEventListenersObserveCommittedSessionState()
  {
    var faction = BattleTestFactory.MakeFaction("Player");
    var session = BattleTestFactory.MakeSession(new Vector3I(4, 1, 4), [faction]);
    var source = new Vector3I(1, 0, 1);
    var destination = new Vector3I(1, 0, 2);
    var unit = SpawnUnit(session, BattleTestFactory.MakeCombatant("Alpha", faction, actionPoints: 5), source);
    StartBattle(session);

    Option<Vector3I> observedPositionDuringEvent = None;
    session.BattleEventCommitted += battleEvent =>
    {
      if (battleEvent is UnitMovedBattleEvent)
        observedPositionDuringEvent = session.GetUnitPosition(unit.Handle).Map(point => point.Raw);
    };

    var executor = new BattleActionExecutor(session);
    var result = executor.Submit(BattleAction.MoveUnitStep(unit.Handle, destination, 2)).RequireSingleResult();

    Assert.True(result.Succeeded);
    Assert.Equal(destination, observedPositionDuringEvent.RequireSome());
  }

  [TestCase(TestName = "Primitive action only yields itself once before completion")]
  public void PrimitiveActionOnlyYieldsItselfOnceBeforeCompletion()
  {
    var faction = BattleTestFactory.MakeFaction("Player");
    var session = BattleTestFactory.MakeSession(new Vector3I(3, 1, 3), [faction]);
    var unit = SpawnUnit(session, BattleTestFactory.MakeCombatant("Alpha", faction), new Vector3I(0, 0, 0));
    StartBattle(session);

    var action = BattleAction.MoveUnitStep(unit.Handle, new Vector3I(1, 0, 0));

    Option<BattleAction> first = action.NextAction(session);
    Option<BattleAction> second = action.NextAction(session);

    Assert.True(first.IsSome);
    Assert.Equal(action, first.RequireSome());
    Assert.True(second.IsNone);
  }

  [TestCase(TestName = "Executor evaluates matching triggers deterministically")]
  public void ExecutorEvaluatesMatchingTriggersDeterministically()
  {
    var faction = BattleTestFactory.MakeFaction("Player");
    var session = BattleTestFactory.MakeSession(new Vector3I(3, 1, 3), [faction]);
    var unit = SpawnUnit(session, BattleTestFactory.MakeCombatant("Alpha", faction), new Vector3I(0, 0, 0));
    StartBattle(session);

    var executor = new BattleActionExecutor(session);
    var log = new List<string>();
    var targetPosition = new Vector3I(1, 0, 0);
    executor.RegisterTrigger(new RecordingTrigger("late", 10, targetPosition, log, "late"), BattleEventType.UnitMoved);
    executor.RegisterTrigger(new RecordingTrigger("first", 0, targetPosition, log, "first"), BattleEventType.UnitMoved);
    executor.RegisterTrigger(new RecordingTrigger("second", 0, targetPosition, log, "second"), BattleEventType.UnitMoved);
    executor.RegisterTrigger(new RecordingTrigger("ignored", -10, targetPosition, log, "ignored"), BattleEventType.UnitDamaged);

    BattleActionResult result = executor.Submit(BattleAction.MoveUnitStep(unit.Handle, targetPosition)).RequireSingleResult();

    Assert.True(result.Succeeded);
    Assert.True(log.SequenceEqual(["first", "second", "late"]));
  }

  [TestCase(TestName = "Executor only evaluates registered event type bucket")]
  public void ExecutorOnlyEvaluatesRegisteredEventTypeBucket()
  {
    var faction = BattleTestFactory.MakeFaction("Player");
    var session = BattleTestFactory.MakeSession(new Vector3I(3, 1, 3), [faction]);
    var unit = SpawnUnit(session, BattleTestFactory.MakeCombatant("Alpha", faction), new Vector3I(0, 0, 0));
    StartBattle(session);

    var executor = new BattleActionExecutor(session);
    var log = new List<string>();
    var targetPosition = new Vector3I(1, 0, 0);
    var ignoredTrigger = new CountingTrigger("ignored", targetPosition, log, "ignored");
    var matchingTrigger = new CountingTrigger("matching", targetPosition, log, "matching");
    executor.RegisterTrigger(ignoredTrigger, BattleEventType.UnitDamaged);
    executor.RegisterTrigger(matchingTrigger, BattleEventType.UnitMoved);

    BattleActionResult result = executor.Submit(BattleAction.MoveUnitStep(unit.Handle, targetPosition)).RequireSingleResult();

    Assert.True(result.Succeeded);
    Assert.Equal(0, ignoredTrigger.MatchCallCount);
    Assert.Equal(1, matchingTrigger.MatchCallCount);
    Assert.True(log.SequenceEqual(["matching"]));
  }

  [TestCase(TestName = "Executor supports one trigger subscribed to multiple event types")]
  public void ExecutorSupportsOneTriggerSubscribedToMultipleEventTypes()
  {
    var faction = BattleTestFactory.MakeFaction("Player");
    var session = BattleTestFactory.MakeSession(new Vector3I(3, 1, 3), [faction]);
    var unit = SpawnUnit(session, BattleTestFactory.MakeCombatant("Alpha", faction), new Vector3I(0, 0, 0));
    StartBattle(session);

    var executor = new BattleActionExecutor(session);
    var log = new List<string>();
    var targetPosition = new Vector3I(1, 0, 0);
    executor.RegisterTrigger(
      new RecordingTrigger("movement-or-occupation", 0, targetPosition, log, "matched"),
      [BattleEventType.UnitMoved, BattleEventType.TileOccupied]);

    BattleActionResult result = executor.Submit(BattleAction.MoveUnitStep(unit.Handle, targetPosition)).RequireSingleResult();

    Assert.True(result.Succeeded);
    Assert.True(log.SequenceEqual(["matched", "matched"]));
  }

  [TestCase(TestName = "Executor consumes resolved triggers before later actions")]
  public void ExecutorConsumesResolvedTriggersBeforeLaterActions()
  {
    var faction = BattleTestFactory.MakeFaction("Player");
    var session = BattleTestFactory.MakeSession(new Vector3I(3, 1, 3), [faction]);
    var start = new Vector3I(0, 0, 0);
    var targetPosition = new Vector3I(1, 0, 0);
    var unit = SpawnUnit(session, BattleTestFactory.MakeCombatant("Alpha", faction, actionPoints: 5), start);
    StartBattle(session);

    var executor = new BattleActionExecutor(session);
    var log = new List<string>();
    executor.RegisterTrigger(new ConsumingTrigger("mine", targetPosition, log, "matched"), BattleEventType.UnitMoved);

    BattleActionResult[] results =
    [
      executor.Submit(BattleAction.MoveUnitStep(unit.Handle, targetPosition)).RequireSingleResult(),
      executor.Submit(BattleAction.MoveUnitStep(unit.Handle, start)).RequireSingleResult(),
      executor.Submit(BattleAction.MoveUnitStep(unit.Handle, targetPosition)).RequireSingleResult(),
    ];

    Assert.Equal(3, results.Length);
    Assert.True(results.All(result => result.Succeeded));
    Assert.True(log.SequenceEqual(["matched"]));
  }

  [TestCase(TestName = "Executor queues interrupt actions in trigger priority order")]
  public void ExecutorQueuesInterruptActionsInTriggerPriorityOrder()
  {
    var faction = BattleTestFactory.MakeFaction("Player");
    var session = BattleTestFactory.MakeSession(new Vector3I(3, 1, 3), [faction]);
    var start = new Vector3I(0, 0, 0);
    var targetPosition = new Vector3I(1, 0, 0);
    var unit = SpawnUnit(session, BattleTestFactory.MakeCombatant("Alpha", faction, health: 10, actionPoints: 5), start);
    StartBattle(session);

    var executor = new BattleActionExecutor(session);
    executor.RegisterTrigger(
      new DamageOnTileOccupiedTrigger("late", targetPosition, unit.Handle, 2, shouldConsume: true) { Priority = 10 },
      BattleEventType.UnitMoved);
    executor.RegisterTrigger(
      new DamageOnTileOccupiedTrigger("first", targetPosition, unit.Handle, 1, shouldConsume: true) { Priority = 0 },
      BattleEventType.UnitMoved);

    IReadOnlyList<BattleActionResult> results = executor.Submit(BattleAction.MoveUnitStep(unit.Handle, targetPosition));
    int[] damageAmounts = results
      .Select(result => result.Action)
      .OfType<ApplyDamage>()
      .Select(damage => damage.Amount)
      .ToArray();

    Assert.Equal(3, results.Count);
    Assert.True(results.All(result => result.Succeeded));
    Assert.True(damageAmounts.SequenceEqual([1, 2]));
    Assert.Equal(0, executor.PendingCount);
  }

  [TestCase(TestName = "Executor queues trigger response after committed tile occupation")]
  public void ExecutorQueuesTriggerResponseAfterCommittedTileOccupation()
  {
    var faction = BattleTestFactory.MakeFaction("Player");
    var session = BattleTestFactory.MakeSession(new Vector3I(3, 1, 3), [faction]);
    var unit = SpawnUnit(session, BattleTestFactory.MakeCombatant("Alpha", faction, health: 10, actionPoints: 5), new Vector3I(0, 0, 0));
    StartBattle(session);

    var executor = new BattleActionExecutor(session);
    var targetPosition = new Vector3I(1, 0, 0);
    var trigger = new DamageOnTileOccupiedTrigger("mine", targetPosition, unit.Handle, 3, shouldConsume: true);
    executor.RegisterTrigger(trigger, BattleEventType.TileOccupied);

    IReadOnlyList<BattleActionResult> results = executor.Submit(BattleAction.MoveUnitStep(unit.Handle, targetPosition));

    Assert.Equal(2, results.Count);
    Assert.True(results.All(result => result.Succeeded));
    Assert.True(results[0].Action is MoveUnitStep);
    Assert.True(results[1].Action is ApplyDamage);
    Assert.Equal(targetPosition, session.GetUnitPosition(unit.Handle).RequireSome().Raw);
    Assert.Equal(targetPosition, trigger.ObservedTargetPositionDuringEvaluation.RequireSome());
    Assert.Equal(7, unit.State.CurrentHealth);
    Assert.Equal(0, executor.PendingCount);
  }

  [TestCase(TestName = "Executor rejected source action does not consume trigger or enqueue response")]
  public void ExecutorRejectedSourceActionDoesNotConsumeTriggerOrEnqueueResponse()
  {
    var faction = BattleTestFactory.MakeFaction("Player");
    var session = BattleTestFactory.MakeSession(new Vector3I(4, 1, 4), [faction]);
    var unit = SpawnUnit(session, BattleTestFactory.MakeCombatant("Alpha", faction, health: 10), new Vector3I(0, 0, 0));
    StartBattle(session);

    var executor = new BattleActionExecutor(session);
    var targetPosition = new Vector3I(2, 0, 0);
    executor.RegisterTrigger(new DamageOnTileOccupiedTrigger("mine", targetPosition, unit.Handle, 3, shouldConsume: true), BattleEventType.TileOccupied);

    BattleActionResult rejectedResult = executor.Submit(BattleAction.MoveUnitStep(unit.Handle, targetPosition)).RequireSingleResult();

    Assert.False(rejectedResult.Succeeded);
    Assert.Equal(new Vector3I(0, 0, 0), session.GetUnitPosition(unit.Handle).RequireSome().Raw);
    Assert.Equal(10, unit.State.CurrentHealth);
    Assert.Equal(0, executor.PendingCount);

    BattleActionResult approachResult = executor.Submit(BattleAction.MoveUnitStep(unit.Handle, new Vector3I(1, 0, 0))).RequireSingleResult();
    IReadOnlyList<BattleActionResult> sourceResults = executor.Submit(BattleAction.MoveUnitStep(unit.Handle, targetPosition));

    Assert.True(approachResult.Succeeded);
    Assert.Equal(2, sourceResults.Count);
    BattleActionResult sourceResult = sourceResults[0];
    BattleActionResult responseResult = sourceResults[1];
    Assert.True(sourceResult.Succeeded);
    Assert.True(responseResult.Succeeded);
    Assert.True(responseResult.Action is ApplyDamage);
    Assert.Equal(7, unit.State.CurrentHealth);
    Assert.Equal(0, executor.PendingCount);
  }

  [TestCase(TestName = "Executor supports multiple trigger instances with the same trigger id")]
  public void ExecutorSupportsMultipleTriggerInstancesWithTheSameTriggerId()
  {
    var faction = BattleTestFactory.MakeFaction("Player");
    var session = BattleTestFactory.MakeSession(new Vector3I(4, 1, 4), [faction]);
    var start = new Vector3I(0, 0, 0);
    var firstTrap = new Vector3I(1, 0, 0);
    var secondTrap = new Vector3I(2, 0, 0);
    var unit = SpawnUnit(session, BattleTestFactory.MakeCombatant("Alpha", faction, health: 10, actionPoints: 5), start);
    StartBattle(session);

    var executor = new BattleActionExecutor(session);
    executor.RegisterTrigger(
      new DamageOnTileOccupiedTrigger("mine", firstTrap, unit.Handle, 2, shouldConsume: true),
      BattleEventType.TileOccupied);
    executor.RegisterTrigger(
      new DamageOnTileOccupiedTrigger("mine", secondTrap, unit.Handle, 3, shouldConsume: true),
      BattleEventType.TileOccupied);

    IReadOnlyList<BattleActionResult> results = executor.Submit(BattleAction.MoveUnit(unit.Handle, [start, firstTrap, secondTrap]));

    Assert.Equal(4, results.Count);
    Assert.True(results.All(result => result.Succeeded));
    Assert.True(results[0].Action is MoveUnitStep);
    Assert.True(results[1].Action is ApplyDamage);
    Assert.True(results[2].Action is MoveUnitStep);
    Assert.True(results[3].Action is ApplyDamage);
    Assert.Equal(5, unit.State.CurrentHealth);
    Assert.Equal(0, executor.PendingCount);
  }

  [TestCase(TestName = "Executor discards non producing action and resolves later queued work")]
  public void ExecutorDiscardsNonProducingActionAndResolvesLaterQueuedWork()
  {
    var faction = BattleTestFactory.MakeFaction("Player");
    var session = BattleTestFactory.MakeSession(new Vector3I(3, 1, 3), [faction]);
    var unit = SpawnUnit(session, BattleTestFactory.MakeCombatant("Alpha", faction, actionPoints: 5), new Vector3I(0, 0, 0));
    StartBattle(session);

    var executor = new BattleActionExecutor(session);
    var nonProducingResult = executor.Submit(new NonProducingBattleAction());
    var result = executor.Submit(BattleAction.MoveUnitStep(unit.Handle, new Vector3I(1, 0, 0)));

    Assert.Equal(0, nonProducingResult.Count);
    BattleActionResult moveResult = result.RequireSingleResult();
    Assert.True(moveResult.Succeeded);
    Assert.True(moveResult.Action is MoveUnitStep);
    Assert.Equal(new Vector3I(1, 0, 0), session.GetUnitPosition(unit.Handle).RequireSome().Raw);
    Assert.Equal(0, executor.PendingCount);
  }

  [TestCase(TestName = "MoveUnit submit processes every move step")]
  public void MoveUnitSubmitProcessesEveryMoveStep()
  {
    var faction = BattleTestFactory.MakeFaction("Player");
    var session = BattleTestFactory.MakeSession(new Vector3I(4, 1, 4), [faction]);
    var start = new Vector3I(0, 0, 0);
    var mid = new Vector3I(1, 0, 0);
    var end = new Vector3I(2, 0, 0);
    var unit = SpawnUnit(session, BattleTestFactory.MakeCombatant("Alpha", faction, actionPoints: 5), start);
    StartBattle(session);

    var action = new MoveUnit(unit.Handle, [start, mid, end]);
    var executor = new BattleActionExecutor(session);
    IReadOnlyList<BattleActionResult> results = executor.Submit(action);

    Assert.Equal(2, results.Count);
    Assert.True(results.All(result => result.Succeeded));
    Assert.True(results.All(result => result.Action is MoveUnitStep));
    Assert.True(results.All(result => result.Action.IsDone()));
    Assert.Equal(end, session.GetUnitPosition(unit.Handle).RequireSome().Raw);
    Assert.True(action.IsDone());
    Assert.Equal(0, executor.PendingCount);
  }

  [TestCase(TestName = "MoveUnit resolves trigger response before resuming")]
  public void MoveUnitResolvesTriggerResponseBeforeResuming()
  {
    var faction = BattleTestFactory.MakeFaction("Player");
    var session = BattleTestFactory.MakeSession(new Vector3I(4, 1, 4), [faction]);
    var start = new Vector3I(0, 0, 0);
    var mid = new Vector3I(1, 0, 0);
    var end = new Vector3I(2, 0, 0);
    var unit = SpawnUnit(session, BattleTestFactory.MakeCombatant("Alpha", faction, health: 10, actionPoints: 5), start);
    StartBattle(session);

    var action = new MoveUnit(unit.Handle, [start, mid, end]);
    var executor = new BattleActionExecutor(session);
    executor.RegisterTrigger(new DamageOnTileOccupiedTrigger("mine", mid, unit.Handle, 3, shouldConsume: true), BattleEventType.TileOccupied);
    IReadOnlyList<BattleActionResult> results = executor.Submit(action);

    Assert.Equal(3, results.Count);
    Assert.True(results.All(result => result.Succeeded));
    Assert.True(results[0].Action is MoveUnitStep);
    Assert.True(results[1].Action is ApplyDamage);
    Assert.True(results[2].Action is MoveUnitStep);
    Assert.Equal(end, session.GetUnitPosition(unit.Handle).RequireSome().Raw);
    Assert.Equal(7, unit.State.CurrentHealth);
    Assert.True(action.IsDone());
    Assert.Equal(0, executor.PendingCount);
  }

  [TestCase(TestName = "MoveUnit cancels when trigger response kills mover")]
  public void MoveUnitCancelsWhenTriggerResponseKillsMover()
  {
    var faction = BattleTestFactory.MakeFaction("Player");
    var session = BattleTestFactory.MakeSession(new Vector3I(4, 1, 4), [faction]);
    var start = new Vector3I(0, 0, 0);
    var mid = new Vector3I(1, 0, 0);
    var end = new Vector3I(2, 0, 0);
    var unit = SpawnUnit(session, BattleTestFactory.MakeCombatant("Alpha", faction, health: 3, actionPoints: 5), start);
    StartBattle(session);

    var action = new MoveUnit(unit.Handle, [start, mid, end]);
    var executor = new BattleActionExecutor(session);
    executor.RegisterTrigger(new DamageOnTileOccupiedTrigger("mine", mid, unit.Handle, 3, shouldConsume: true), BattleEventType.TileOccupied);
    IReadOnlyList<BattleActionResult> results = executor.Submit(action);

    Assert.Equal(2, results.Count);
    Assert.True(results.All(result => result.Succeeded));
    Assert.True(results[0].Action is MoveUnitStep);
    Assert.True(results[1].Action is ApplyDamage);
    Assert.True(action.IsDone());
    Assert.Equal(0, unit.State.CurrentHealth);
    Assert.True(session.GetUnitPosition(unit.Handle).IsNone);
    Assert.Equal(0, executor.PendingCount);
  }

  [TestCase(TestName = "MoveUnit fails malformed path before moving")]
  public void MoveUnitFailsMalformedPathBeforeMoving()
  {
    var faction = BattleTestFactory.MakeFaction("Player");
    var session = BattleTestFactory.MakeSession(new Vector3I(4, 1, 4), [faction]);
    var start = new Vector3I(0, 0, 0);
    var mid = new Vector3I(1, 0, 0);
    var nonAdjacent = new Vector3I(3, 0, 0);
    var unit = SpawnUnit(session, BattleTestFactory.MakeCombatant("Alpha", faction, actionPoints: 5), start);
    StartBattle(session);

    var action = new MoveUnit(unit.Handle, [start, mid, nonAdjacent]);
    var executor = new BattleActionExecutor(session);
    var result = executor.Submit(action);

    Assert.Equal(0, result.Count);
    Assert.True(action.IsDone());
    Assert.Equal(start, session.GetUnitPosition(unit.Handle).RequireSome().Raw);
    Assert.Equal(5, unit.State.CurrentActionPoints);
    Assert.Equal(0, executor.PendingCount);
  }

  [TestCase(TestName = "MoveUnit fails one point path that starts away from the unit")]
  public void MoveUnitFailsOnePointPathThatStartsAwayFromTheUnit()
  {
    var faction = BattleTestFactory.MakeFaction("Player");
    var session = BattleTestFactory.MakeSession(new Vector3I(3, 1, 3), [faction]);
    var start = new Vector3I(0, 0, 0);
    var other = new Vector3I(1, 0, 0);
    var unit = SpawnUnit(session, BattleTestFactory.MakeCombatant("Alpha", faction, actionPoints: 5), start);
    StartBattle(session);

    var action = new MoveUnit(unit.Handle, [other]);
    var executor = new BattleActionExecutor(session);
    var result = executor.Submit(action);

    Assert.Equal(0, result.Count);
    Assert.True(action.IsDone());
    Assert.Equal(start, session.GetUnitPosition(unit.Handle).RequireSome().Raw);
    Assert.Equal(0, executor.PendingCount);
  }

  [TestCase(TestName = "EndFactionTurn emits turn transition events after commit")]
  public void EndFactionTurnEmitsTurnTransitionEventsAfterCommit()
  {
    var factionA = BattleTestFactory.MakeFaction("A");
    var factionB = BattleTestFactory.MakeFaction("B");
    var session = BattleTestFactory.MakeSession(new Vector3I(4, 1, 4), [factionA, factionB]);
    SpawnUnit(session, BattleTestFactory.MakeCombatant("A1", factionA), new Vector3I(0, 0, 0));
    SpawnUnit(session, BattleTestFactory.MakeCombatant("B1", factionB), new Vector3I(1, 0, 0));
    StartBattle(session);

    var raisedEvents = new List<BattleEvent>();
    session.BattleEventCommitted += raisedEvents.Add;

    var executor = new BattleActionExecutor(session);
    BattleActionResult result = executor.Submit(BattleAction.EndFactionTurn(factionA)).RequireSingleResult();

    Assert.True(result.Succeeded);
    Assert.True(raisedEvents.OfType<TurnEndedBattleEvent>().Any(battleEvent =>
      battleEvent.Faction == factionA &&
      battleEvent.TurnNumber == 1));
    Assert.True(raisedEvents.OfType<TurnStartedBattleEvent>().Any(battleEvent =>
      battleEvent.Faction == factionB &&
      battleEvent.TurnNumber == 1));
  }

  [TestCase(TestName = "Executor resolves queued actions in FIFO order")]
  public void ExecutorResolvesQueuedActionsInFifoOrder()
  {
    var factionA = BattleTestFactory.MakeFaction("A");
    var factionB = BattleTestFactory.MakeFaction("B");
    var session = BattleTestFactory.MakeSession(new Vector3I(4, 1, 4), [factionA, factionB]);
    var unitA = SpawnUnit(session, BattleTestFactory.MakeCombatant("A1", factionA, actionPoints: 4), new Vector3I(0, 0, 0));
    SpawnUnit(session, BattleTestFactory.MakeCombatant("B1", factionB), new Vector3I(1, 0, 0));
    StartBattle(session);

    var executor = new BattleActionExecutor(session);
    BattleActionResult first = executor.Submit(BattleAction.MoveUnitStep(unitA.Handle, new Vector3I(0, 0, 1))).RequireSingleResult();
    BattleActionResult second = executor.Submit(BattleAction.EndFactionTurn(factionA)).RequireSingleResult();

    Assert.True(first.Succeeded);
    Assert.True(first.Action is MoveUnitStep);
    Assert.True(second.Succeeded);
    Assert.True(second.Action is EndFactionTurn);
    Assert.Equal(factionB, session.ActiveSide);
  }

  [TestCase(TestName = "Executor resolves pass unit while keeping the next ally available")]
  public void ExecutorResolvesPassUnitWhileKeepingTheNextAllyAvailable()
  {
    var faction = BattleTestFactory.MakeFaction("Player");
    var session = BattleTestFactory.MakeSession(new Vector3I(4, 1, 4), [faction]);
    var unitA = SpawnUnit(session, BattleTestFactory.MakeCombatant("A1", faction), new Vector3I(0, 0, 0));
    var unitB = SpawnUnit(session, BattleTestFactory.MakeCombatant("A2", faction), new Vector3I(1, 0, 0));
    StartBattle(session);

    var executor = new BattleActionExecutor(session);
    BattleActionResult result = executor.Submit(BattleAction.PassUnit(unitA.Handle)).RequireSingleResult();

    Assert.True(result.Succeeded);
    Assert.False(GetValue(session.Queries.Execute(new IsUnitStillAvailableThisTurn(unitA.Handle))));
    Assert.False(GetValue(session.Queries.Execute(new CanUnitActNow(unitA.Handle))));
    Assert.True(GetValue(session.Queries.Execute(new IsUnitStillAvailableThisTurn(unitB.Handle))));
    Assert.Equal(faction, session.ActiveSide);
  }

  [TestCase(TestName = "Executor returns rejected action results cleanly")]
  public void ExecutorReturnsRejectedActionResultsCleanly()
  {
    var faction = BattleTestFactory.MakeFaction("Player");
    var session = BattleTestFactory.MakeSession(new Vector3I(3, 1, 3), [faction]);
    var unit = SpawnUnit(session, BattleTestFactory.MakeCombatant("Alpha", faction), new Vector3I(0, 0, 0));
    StartBattle(session);

    var executor = new BattleActionExecutor(session);
    BattleActionResult result = executor.Submit(BattleAction.MoveUnitStep(unit.Handle, new Vector3I(2, 0, 0))).RequireSingleResult();

    Assert.False(result.Succeeded);
    Assert.Equal(BattleActionFailureReason.Rejected, result.FailureReason);
  }

  [TestCase(TestName = "Executor emits action complete events")]
  public void ExecutorEmitsActionCompleteEvents()
  {
    var faction = BattleTestFactory.MakeFaction("Player");
    var session = BattleTestFactory.MakeSession(new Vector3I(4, 1, 4), [faction]);
    var unit = SpawnUnit(session, BattleTestFactory.MakeCombatant("Alpha", faction), new Vector3I(1, 0, 1));
    StartBattle(session);

    var executor = new BattleActionExecutor(session);
    Option<BattleActionResult> resolvedResult = None;
    executor.OnActionComplete += result => resolvedResult = Some(result);

    executor.Submit(BattleAction.MoveUnitStep(unit.Handle, new Vector3I(1, 0, 2)));

    Assert.True(resolvedResult.IsSome);
    Assert.True(resolvedResult.RequireSome().Succeeded);
    Assert.True(resolvedResult.RequireSome().Action is MoveUnitStep);
  }

  [TestCase(TestName = "Executor submit returns each submitted action result")]
  public void ExecutorSubmitReturnsEachSubmittedActionResult()
  {
    var factionA = BattleTestFactory.MakeFaction("A");
    var factionB = BattleTestFactory.MakeFaction("B");
    var session = BattleTestFactory.MakeSession(new Vector3I(5, 1, 5), [factionA, factionB]);
    var unitA = SpawnUnit(session, BattleTestFactory.MakeCombatant("A1", factionA, actionPoints: 5), new Vector3I(0, 0, 0));
    SpawnUnit(session, BattleTestFactory.MakeCombatant("B1", factionB), new Vector3I(3, 0, 0));
    var grenade = BattleTestFactory.MakeGrenade("Practice");
    unitA.AddInventoryItem(grenade);
    StartBattle(session);

    var executor = new BattleActionExecutor(session);
    BattleActionResult[] results =
    [
      executor.Submit(BattleAction.MoveUnitStep(unitA.Handle, new Vector3I(0, 0, 1))).RequireSingleResult(),
      executor.Submit(BattleAction.ThrowItem(unitA.Handle, grenade, new Vector3I(2, 0, 1))).RequireSingleResult(),
      executor.Submit(BattleAction.EndFactionTurn(factionA)).RequireSingleResult(),
    ];

    Assert.Equal(3, results.Length);
    Assert.True(results[0].Succeeded);
    Assert.True(results[1].Succeeded);
    Assert.True(results[2].Succeeded);
    Assert.Equal(factionB, session.ActiveSide);
    Assert.False(unitA.HasInventoryItem(grenade));
  }

  [TestCase(TestName = "Executor rejects stale end faction turn action after auto-advance")]
  public void ExecutorRejectsStaleEndFactionTurnActionAfterAutoAdvance()
  {
    var factionA = BattleTestFactory.MakeFaction("A");
    var factionB = BattleTestFactory.MakeFaction("B");
    var session = BattleTestFactory.MakeSession(new Vector3I(4, 1, 4), [factionA, factionB]);
    var unitA = SpawnUnit(session, BattleTestFactory.MakeCombatant("A1", factionA), new Vector3I(0, 0, 0));
    SpawnUnit(session, BattleTestFactory.MakeCombatant("B1", factionB), new Vector3I(1, 0, 0));
    StartBattle(session);

    var executor = new BattleActionExecutor(session);
    BattleActionResult first = executor.Submit(BattleAction.PassUnit(unitA.Handle)).RequireSingleResult();
    BattleActionResult second = executor.Submit(BattleAction.EndFactionTurn(factionA)).RequireSingleResult();

    Assert.True(first.Succeeded);
    Assert.Equal(factionB, session.ActiveSide);
    Assert.False(second.Succeeded);
    Assert.Equal(BattleActionFailureReason.Rejected, second.FailureReason);
    Assert.Equal(factionB, session.ActiveSide);
  }

  [TestCase(TestName = "Executor recovers when OnActionStart handler throws")]
  public void ExecutorRecoversWhenOnActionStartHandlerThrows()
  {
    var faction = BattleTestFactory.MakeFaction("Player");
    var session = BattleTestFactory.MakeSession(new Vector3I(4, 2, 4), [faction]);
    var unit = SpawnUnit(session, BattleTestFactory.MakeCombatant("Alpha", faction, actionPoints: 5), new Vector3I(1, 0, 1));
    StartBattle(session);

    var executor = new BattleActionExecutor(session);
    bool shouldThrow = true;
    executor.OnActionStart += _ =>
    {
      if (!shouldThrow)
        return;

      shouldThrow = false;
      throw new InvalidOperationException("boom");
    };

    BattleActionResult first = executor.Submit(BattleAction.MoveUnitStep(unit.Handle, new Vector3I(1, 0, 2), 2)).RequireSingleResult();
    BattleActionResult second = executor.Submit(BattleAction.MoveUnitStep(unit.Handle, new Vector3I(1, 1, 1), 2)).RequireSingleResult();

    Assert.False(first.Succeeded);
    Assert.Equal(BattleActionFailureReason.UnexpectedError, first.FailureReason);

    Assert.True(second.Succeeded);
    Assert.Equal(new Vector3I(1, 1, 1), session.GetUnitPosition(unit.Handle).RequireSome().Raw);
  }

  [TestCase(TestName = "Executor returns no results when submitted action produces none")]
  public void ExecutorReturnsNoResultsWhenSubmittedActionProducesNone()
  {
    var faction = BattleTestFactory.MakeFaction("Player");
    var session = BattleTestFactory.MakeSession(new Vector3I(2, 1, 2), [faction]);
    var executor = new BattleActionExecutor(session);

    var result = executor.Submit(new NonProducingBattleAction());

    Assert.Equal(0, result.Count);
    Assert.True(executor.LastResult.IsNone);
  }

  private sealed partial class RecordingTrigger : BattleTrigger
  {
    private readonly Vector3I _position;
    private readonly List<string> _log;
    private readonly string _message;

    public RecordingTrigger(string triggerId, int priority, Vector3I position, List<string> log, string message)
      : base(triggerId, priority)
    {
      _position = position;
      _log = log;
      _message = message;
    }

    public override bool Matches(BattleEvent battleEvent)
    {
      return battleEvent is IPositionedBattleEvent positioned && positioned.Position.Raw == _position;
    }

    public override BattleTriggerResult Evaluate(BattleSession session, BattleEvent battleEvent, BattleAction sourceAction)
    {
      _log.Add(_message);
      return BattleTriggerResult.NoReaction();
    }
  }

  private sealed partial class CountingTrigger : BattleTrigger
  {
    private readonly Vector3I _position;
    private readonly List<string> _log;
    private readonly string _message;

    public int MatchCallCount { get; private set; }

    public CountingTrigger(string triggerId, Vector3I position, List<string> log, string message)
      : base(triggerId)
    {
      _position = position;
      _log = log;
      _message = message;
    }

    public override bool Matches(BattleEvent battleEvent)
    {
      MatchCallCount++;
      return battleEvent is IPositionedBattleEvent positioned && positioned.Position.Raw == _position;
    }

    public override BattleTriggerResult Evaluate(BattleSession session, BattleEvent battleEvent, BattleAction sourceAction)
    {
      _log.Add(_message);
      return BattleTriggerResult.NoReaction();
    }
  }

  private sealed partial class ConsumingTrigger : BattleTrigger
  {
    private readonly Vector3I _position;
    private readonly List<string> _log;
    private readonly string _message;

    public ConsumingTrigger(string triggerId, Vector3I position, List<string> log, string message)
      : base(triggerId)
    {
      _position = position;
      _log = log;
      _message = message;
    }

    public override bool Matches(BattleEvent battleEvent)
    {
      return battleEvent is IPositionedBattleEvent positioned && positioned.Position.Raw == _position;
    }

    public override BattleTriggerResult Evaluate(BattleSession session, BattleEvent battleEvent, BattleAction sourceAction)
    {
      _log.Add(_message);
      return BattleTriggerResult.ConsumeTrigger();
    }
  }

  private sealed partial class DamageOnTileOccupiedTrigger : BattleTrigger
  {
    private readonly Vector3I _position;
    private readonly BattleSession.BattleUnitHandle _targetHandle;
    private readonly int _damage;
    private readonly bool _shouldConsume;

    public Option<Vector3I> ObservedTargetPositionDuringEvaluation { get; private set; }

    public DamageOnTileOccupiedTrigger(
      string triggerId,
      Vector3I position,
      BattleSession.BattleUnitHandle targetHandle,
      int damage,
      bool shouldConsume)
      : base(triggerId)
    {
      _position = position;
      _targetHandle = targetHandle;
      _damage = damage;
      _shouldConsume = shouldConsume;
    }

    public override bool Matches(BattleEvent battleEvent)
    {
      return battleEvent is IPositionedBattleEvent positioned && positioned.Position.Raw == _position;
    }

    public override BattleTriggerResult Evaluate(BattleSession session, BattleEvent battleEvent, BattleAction sourceAction)
    {
      ObservedTargetPositionDuringEvaluation = session.GetUnitPosition(_targetHandle)
        .Match(point => Some(point.Raw), () => None);

      return BattleTriggerResult.QueueInterruptAfterCommit(
        BattleAction.ApplyDamage(_targetHandle, _damage),
        _shouldConsume);
    }
  }

  private sealed partial class DamageOnMovementEventTrigger : BattleTrigger
  {
    private readonly Vector3I _position;
    private readonly BattleSession.BattleUnitHandle _targetHandle;
    private readonly int _damage;

    public DamageOnMovementEventTrigger(
      string triggerId,
      Vector3I position,
      BattleSession.BattleUnitHandle targetHandle,
      int damage)
      : base(triggerId)
    {
      _position = position;
      _targetHandle = targetHandle;
      _damage = damage;
    }

    public override bool Matches(BattleEvent battleEvent)
    {
      return battleEvent is IPositionedBattleEvent positioned && positioned.Position.Raw == _position;
    }

    public override BattleTriggerResult Evaluate(BattleSession session, BattleEvent battleEvent, BattleAction sourceAction)
    {
      return BattleTriggerResult.QueueInterruptAfterCommit(
        BattleAction.ApplyDamage(_targetHandle, _damage),
        shouldConsumeTrigger: true);
    }
  }

  private sealed class NonProducingBattleAction : BattleAction
  {
    public NonProducingBattleAction()
      : base("non_producing_test_action")
    {
    }

    public override Option<BattleAction> NextAction(BattleSession session)
    {
      return None;
    }

    public override void ConsumeResult(BattleActionResult result)
    {
    }
  }
}
