using FunProject.Battle;
using FunProject.Combatants;
using GdUnit4;
using Godot;
using System.Collections.Generic;

[TestSuite]
[RequireGodotRuntime]
public partial class BattleActionExecutorTest
{
  [TestCase(TestName = "Submit commits source action and reaction actions")]
  public void SubmitCommitsSourceActionAndReactionActions()
  {
    var (session, executor, _, unit) = StartSoloBattle(new Vector3I(3, 1, 3), new Vector3I(0, 0, 0), health: 10, actionPoints: 5);
    var targetPosition = new Vector3I(1, 0, 0);
    session.RegisterHook<TileOccupiedBattleEvent>(new DamageOnTileOccupiedHook<TileOccupiedBattleEvent>(targetPosition, unit.State, 3, oneShot: true), HookPhase.After);
    var resolvedActions = new List<BattleAction>();
    executor.OnActionComplete += result => resolvedActions.Add(result.Action);

    BattleAction submittedAction = BattleAction.MoveUnit(unit.State, [session.Board.At(targetPosition)]);
    IReadOnlyList<BattleActionResult> results = executor.Submit(submittedAction);

    Assert.Equal(2, results.Count);
    Assert.Equal(submittedAction, results[0].Action);
    Assert.True(results[0].Succeeded);
    Assert.True(results[1].Succeeded);
    Assert.True(results[1].Action is ApplyDamage);
    Assert.Equal(targetPosition, session.GetUnitPosition(unit.State).RequireSome().Raw);
    Assert.Equal(7, unit.State.CurrentHealth);
    Assert.True(resolvedActions.Select(action => action.ActionId).SequenceEqual([
      "move_unit",
      FunProject.Battle.ApplyDamage.ApplyDamageActionId,
    ]));
  }

  [TestCase(TestName = "Submit reports composite actions without intermediate primitive actions")]
  public void SubmitReportsCompositeActionsWithoutIntermediatePrimitiveActions()
  {
    var start = new Vector3I(0, 0, 0);
    var mid = new Vector3I(1, 0, 0);
    var end = new Vector3I(2, 0, 0);
    var (session, executor, _, unit) = StartSoloBattle(new Vector3I(4, 1, 4), start, health: 10, actionPoints: 5);
    session.RegisterHook<TileOccupiedBattleEvent>(new DamageOnTileOccupiedHook<TileOccupiedBattleEvent>(mid, unit.State, 3, oneShot: true), HookPhase.After);
    var resolvedActions = new List<BattleAction>();
    executor.OnActionComplete += result => resolvedActions.Add(result.Action);

    BattleAction submittedAction = new MoveUnit(unit.State, [session.Board.At(mid), session.Board.At(end)]);
    IReadOnlyList<BattleActionResult> results = executor.Submit(submittedAction);

    Assert.Equal(2, results.Count);
    Assert.True(results.All(result => result.Succeeded));
    Assert.True(results[0].Action is ApplyDamage);
    Assert.Equal(submittedAction, results[1].Action);
    Assert.Equal(end, session.GetUnitPosition(unit.State).RequireSome().Raw);
    Assert.Equal(7, unit.State.CurrentHealth);
    Assert.True(submittedAction.IsDone());
    Assert.True(resolvedActions.Select(action => action.ActionId).SequenceEqual([
      FunProject.Battle.ApplyDamage.ApplyDamageActionId,
      "move_unit",
    ]));
    Assert.Equal(submittedAction, executor.LastResult.RequireSome().Action);
  }

  [TestCase(TestName = "MoveUnit emits movement and tile occupation events after commit")]
  public void MoveUnitEmitsMovementAndTileOccupationEventsAfterCommit()
  {
    var (session, executor, _, unit) = StartSoloBattle(new Vector3I(4, 1, 4), new Vector3I(1, 0, 1), actionPoints: 5);
    var recorder = new BattleEventRecorder(session);

    var result = executor.Submit(BattleAction.MoveUnit(unit.State, [session.Board.At(1, 0, 2)], 2));

    Assert.True(result.First().Succeeded);
    Assert.True(recorder.OfType<UnitMovedBattleEvent>().Any(battleEvent =>
      battleEvent.Position.Raw == new Vector3I(1, 0, 2) &&
      battleEvent.SourcePosition.Raw == new Vector3I(1, 0, 1)));
    Assert.True(recorder.OfType<TileOccupiedBattleEvent>().Any(battleEvent =>
      battleEvent.Position.Raw == new Vector3I(1, 0, 2)));
  }

  [TestCase(TestName = "MoveUnit rejects units that cannot act now")]
  public void MoveUnitRejectsUnitsThatCannotActNow()
  {
    var playerFaction = BattleTestFactory.MakeFaction("Player");
    var enemyFaction = BattleTestFactory.MakeFaction("Enemy");
    var session = BattleTestFactory.MakeSession(new Vector3I(4, 1, 4), [playerFaction, enemyFaction]);
    var start = new Vector3I(2, 0, 0);
    var destination = new Vector3I(3, 0, 0);
    SpawnUnit(session, BattleTestFactory.MakeCombatant("Player", playerFaction), new Vector3I(0, 0, 0));
    var enemyUnit = SpawnUnit(session, BattleTestFactory.MakeCombatant("Enemy", enemyFaction), start);
    StartBattle(session);

    var executor = new BattleActionExecutor(session);
    IReadOnlyList<BattleActionResult> results = executor.Submit(BattleAction.MoveUnit(enemyUnit.State, [session.Board.At(destination)]));

    Assert.Equal(0, results.Count);
    Assert.Equal(playerFaction, session.ActiveSide);
    Assert.Equal(start, session.GetUnitPosition(enemyUnit.State).RequireSome().Raw);
  }

  [TestCase(TestName = "BattleEventCommitted subscribers observe committed session state")]
  public void BattleEventCommittedSubscribersObserveCommittedSessionState()
  {
    var (session, executor, _, unit) = StartSoloBattle(new Vector3I(4, 1, 4), new Vector3I(1, 0, 1), actionPoints: 5);
    var destination = new Vector3I(1, 0, 2);

    Option<Vector3I> observedPositionDuringEvent = None;
    session.BattleEventCommitted += battleEvent =>
    {
      if (battleEvent is UnitMovedBattleEvent)
        observedPositionDuringEvent = session.GetUnitPosition(unit.State).Map(point => point.Raw);
    };

    var result = executor.Submit(BattleAction.MoveUnit(unit.State, [session.Board.At(destination)], 2)).RequireSingleResult();

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

    var action = BattleAction.ApplyDamage(unit.State, 1);

    Option<BattleAction> first = action.NextAction(session);
    Option<BattleAction> second = action.NextAction(session);

    Assert.True(first.IsSome);
    Assert.Equal(action, first.RequireSome());
    Assert.True(second.IsNone);
  }

  [TestCase(TestName = "Executor evaluates matching hooks deterministically")]
  public void ExecutorEvaluatesMatchingHooksDeterministically()
  {
    var (session, executor, _, unit) = StartSoloBattle(new Vector3I(3, 1, 3), new Vector3I(0, 0, 0));
    var log = new List<string>();
    var targetPosition = new Vector3I(1, 0, 0);
    session.RegisterHook<UnitMovedBattleEvent>(new PositionRecordingHook(targetPosition, log, "late"), HookPhase.After, priority: 10);
    session.RegisterHook<UnitMovedBattleEvent>(new PositionRecordingHook(targetPosition, log, "first"), HookPhase.After);
    session.RegisterHook<UnitMovedBattleEvent>(new PositionRecordingHook(targetPosition, log, "second"), HookPhase.After);
    session.RegisterHook<UnitDamagedBattleEvent>(new PositionRecordingHook(targetPosition, log, "ignored"), HookPhase.After, priority: -10);

    BattleActionResult result = executor.Submit(BattleAction.MoveUnit(unit.State, [session.Board.At(targetPosition)])).RequireSingleResult();

    Assert.True(result.Succeeded);
    Assert.True(log.SequenceEqual(["first", "second", "late"]));
  }

  [TestCase(TestName = "Executor only evaluates registered event key bucket")]
  public void ExecutorOnlyEvaluatesRegisteredEventKeyBucket()
  {
    var (session, executor, _, unit) = StartSoloBattle(new Vector3I(3, 1, 3), new Vector3I(0, 0, 0));
    var log = new List<string>();
    var targetPosition = new Vector3I(1, 0, 0);
    var ignoredHook = new PositionRecordingHook(targetPosition, log, "ignored");
    var matchingHook = new PositionRecordingHook(targetPosition, log, "matching");
    session.RegisterHook<UnitDamagedBattleEvent>(ignoredHook, HookPhase.After);
    session.RegisterHook<UnitMovedBattleEvent>(matchingHook, HookPhase.After);

    BattleActionResult result = executor.Submit(BattleAction.MoveUnit(unit.State, [session.Board.At(targetPosition)])).RequireSingleResult();

    Assert.True(result.Succeeded);
    Assert.Equal(0, ignoredHook.EvaluateCallCount);
    Assert.Equal(1, matchingHook.EvaluateCallCount);
    Assert.True(log.SequenceEqual(["matching"]));
  }

  [TestCase(TestName = "Executor supports hook registered to event shape")]
  public void ExecutorSupportsHookRegisteredToEventShape()
  {
    var (session, executor, _, unit) = StartSoloBattle(new Vector3I(3, 1, 3), new Vector3I(0, 0, 0));
    var log = new List<string>();
    var targetPosition = new Vector3I(1, 0, 0);
    session.RegisterHook<IPositionedBattleEvent>(new PositionRecordingHook(targetPosition, log, "matched"), HookPhase.After);

    BattleActionResult result = executor.Submit(BattleAction.MoveUnit(unit.State, [session.Board.At(targetPosition)])).RequireSingleResult();

    Assert.True(result.Succeeded);
    Assert.True(log.SequenceEqual(["matched", "matched"]));
  }

  [TestCase(TestName = "Executor consumes one-shot hooks before later actions")]
  public void ExecutorConsumesOneShotHooksBeforeLaterActions()
  {
    var start = new Vector3I(0, 0, 0);
    var targetPosition = new Vector3I(1, 0, 0);
    var (session, executor, _, unit) = StartSoloBattle(new Vector3I(3, 1, 3), start, actionPoints: 5);
    var log = new List<string>();
    session.RegisterHook<UnitMovedBattleEvent>(new OneShotRecordingHook(targetPosition, log, "matched"), HookPhase.After);

    BattleActionResult[] results =
    [
      executor.Submit(BattleAction.MoveUnit(unit.State, [session.Board.At(targetPosition)])).RequireSingleResult(),
      executor.Submit(BattleAction.MoveUnit(unit.State, [session.Board.At(start)])).RequireSingleResult(),
      executor.Submit(BattleAction.MoveUnit(unit.State, [session.Board.At(targetPosition)])).RequireSingleResult(),
    ];

    Assert.Equal(3, results.Length);
    Assert.True(results.All(result => result.Succeeded));
    Assert.True(log.SequenceEqual(["matched"]));
  }

  [TestCase(TestName = "Executor queues interrupt actions in hook priority order")]
  public void ExecutorQueuesInterruptActionsInHookPriorityOrder()
  {
    var start = new Vector3I(0, 0, 0);
    var targetPosition = new Vector3I(1, 0, 0);
    var (session, executor, _, unit) = StartSoloBattle(new Vector3I(3, 1, 3), start, health: 10, actionPoints: 5);
    session.RegisterHook<UnitMovedBattleEvent>(
      new DamageOnTileOccupiedHook<UnitMovedBattleEvent>(targetPosition, unit.State, 2, oneShot: true), HookPhase.After, priority: 10);
    session.RegisterHook<UnitMovedBattleEvent>(
      new DamageOnTileOccupiedHook<UnitMovedBattleEvent>(targetPosition, unit.State, 1, oneShot: true), HookPhase.After, priority: 0);

    IReadOnlyList<BattleActionResult> results = executor.Submit(BattleAction.MoveUnit(unit.State, [session.Board.At(targetPosition)]));
    int[] damageAmounts = results
      .Select(result => result.Action)
      .OfType<ApplyDamage>()
      .Select(damage => damage.Amount)
      .ToArray();

    Assert.Equal(3, results.Count);
    Assert.True(results.All(result => result.Succeeded));
    Assert.True(damageAmounts.SequenceEqual([1, 2]));
  }

  [TestCase(TestName = "Executor resolves interrupts across a step's events in commit order")]
  public void ExecutorResolvesInterruptsAcrossEventsInCommitOrder()
  {
    var start = new Vector3I(0, 0, 0);
    var targetPosition = new Vector3I(1, 0, 0);
    var (session, executor, _, unit) = StartSoloBattle(new Vector3I(3, 1, 3), start, health: 10, actionPoints: 5);
    // One MoveUnitStep commits UnitMoved then TileOccupied. A hook on each event pins the
    // aggregate-reverse-once contract: the FIRST event's interrupt resolves first. A per-event
    // reverse would flip this to [2, 1].
    session.RegisterHook<UnitMovedBattleEvent>(
      new DamageOnTileOccupiedHook<UnitMovedBattleEvent>(targetPosition, unit.State, 1, oneShot: true), HookPhase.After);
    session.RegisterHook<TileOccupiedBattleEvent>(
      new DamageOnTileOccupiedHook<TileOccupiedBattleEvent>(targetPosition, unit.State, 2, oneShot: true), HookPhase.After);

    IReadOnlyList<BattleActionResult> results = executor.Submit(BattleAction.MoveUnit(unit.State, [session.Board.At(targetPosition)]));
    int[] damageAmounts = results
      .Select(result => result.Action)
      .OfType<ApplyDamage>()
      .Select(damage => damage.Amount)
      .ToArray();

    Assert.Equal(3, results.Count);
    Assert.True(results.All(result => result.Succeeded));
    Assert.True(damageAmounts.SequenceEqual([1, 2]));
  }

  [TestCase(TestName = "Executor queues reaction response after committed tile occupation")]
  public void ExecutorQueuesReactionResponseAfterCommittedTileOccupation()
  {
    var (session, executor, _, unit) = StartSoloBattle(new Vector3I(3, 1, 3), new Vector3I(0, 0, 0), health: 10, actionPoints: 5);
    var targetPosition = new Vector3I(1, 0, 0);
    var reaction = new DamageOnTileOccupiedHook<TileOccupiedBattleEvent>(targetPosition, unit.State, 3, oneShot: true);
    session.RegisterHook<TileOccupiedBattleEvent>(reaction, HookPhase.After);

    IReadOnlyList<BattleActionResult> results = executor.Submit(BattleAction.MoveUnit(unit.State, [session.Board.At(targetPosition)]));

    Assert.Equal(2, results.Count);
    Assert.True(results.All(result => result.Succeeded));
    Assert.True(results[0].Action is MoveUnit);
    Assert.True(results[1].Action is ApplyDamage);
    Assert.Equal(targetPosition, session.GetUnitPosition(unit.State).RequireSome().Raw);
    Assert.Equal(targetPosition, reaction.ObservedTargetPositionDuringEvaluation.RequireSome());
    Assert.Equal(7, unit.State.CurrentHealth);
  }

  [TestCase(TestName = "Executor rejected source action does not consume one-shot hook or enqueue response")]
  public void ExecutorRejectedSourceActionDoesNotConsumeOneShotHookOrEnqueueResponse()
  {
    var (session, executor, _, unit) = StartSoloBattle(new Vector3I(4, 1, 4), new Vector3I(0, 0, 0), health: 10);
    var targetPosition = new Vector3I(2, 0, 0);
    session.RegisterHook<TileOccupiedBattleEvent>(new DamageOnTileOccupiedHook<TileOccupiedBattleEvent>(targetPosition, unit.State, 3, oneShot: true), HookPhase.After);

    IReadOnlyList<BattleActionResult> rejectedResults = executor.Submit(BattleAction.MoveUnit(unit.State, [session.Board.At(targetPosition)]));

    Assert.Equal(0, rejectedResults.Count);
    Assert.Equal(new Vector3I(0, 0, 0), session.GetUnitPosition(unit.State).RequireSome().Raw);
    Assert.Equal(10, unit.State.CurrentHealth);

    BattleActionResult approachResult = executor.Submit(BattleAction.MoveUnit(unit.State, [session.Board.At(1, 0, 0)])).RequireSingleResult();
    IReadOnlyList<BattleActionResult> sourceResults = executor.Submit(BattleAction.MoveUnit(unit.State, [session.Board.At(targetPosition)]));

    Assert.True(approachResult.Succeeded);
    Assert.Equal(2, sourceResults.Count);
    BattleActionResult sourceResult = sourceResults[0];
    BattleActionResult responseResult = sourceResults[1];
    Assert.True(sourceResult.Succeeded);
    Assert.True(responseResult.Succeeded);
    Assert.True(responseResult.Action is ApplyDamage);
    Assert.Equal(7, unit.State.CurrentHealth);
  }

  [TestCase(TestName = "Executor supports multiple one-shot hook instances")]
  public void ExecutorSupportsMultipleOneShotHookInstances()
  {
    var start = new Vector3I(0, 0, 0);
    var firstTrap = new Vector3I(1, 0, 0);
    var secondTrap = new Vector3I(2, 0, 0);
    var (session, executor, _, unit) = StartSoloBattle(new Vector3I(4, 1, 4), start, health: 10, actionPoints: 5);
    session.RegisterHook<TileOccupiedBattleEvent>(
      new DamageOnTileOccupiedHook<TileOccupiedBattleEvent>(firstTrap, unit.State, 2, oneShot: true), HookPhase.After);
    session.RegisterHook<TileOccupiedBattleEvent>(
      new DamageOnTileOccupiedHook<TileOccupiedBattleEvent>(secondTrap, unit.State, 3, oneShot: true), HookPhase.After);

    IReadOnlyList<BattleActionResult> results = executor.Submit(BattleAction.MoveUnit(unit.State, [session.Board.At(firstTrap), session.Board.At(secondTrap)]));

    Assert.Equal(3, results.Count);
    Assert.True(results.All(result => result.Succeeded));
    Assert.True(results[0].Action is ApplyDamage);
    Assert.True(results[1].Action is MoveUnit);
    Assert.True(results[2].Action is ApplyDamage);
    Assert.Equal(5, unit.State.CurrentHealth);
  }

  [TestCase(TestName = "Executor discards non producing action and resolves later queued work")]
  public void ExecutorDiscardsNonProducingActionAndResolvesLaterQueuedWork()
  {
    var (session, executor, _, unit) = StartSoloBattle(new Vector3I(3, 1, 3), new Vector3I(0, 0, 0), actionPoints: 5);
    var nonProducingResult = executor.Submit(new NonProducingBattleAction());
    var result = executor.Submit(BattleAction.MoveUnit(unit.State, [session.Board.At(1, 0, 0)]));

    Assert.Equal(0, nonProducingResult.Count);
    BattleActionResult moveResult = result.RequireSingleResult();
    Assert.True(moveResult.Succeeded);
    Assert.True(moveResult.Action is MoveUnit);
    Assert.Equal(new Vector3I(1, 0, 0), session.GetUnitPosition(unit.State).RequireSome().Raw);
  }

  [TestCase(TestName = "MoveUnit submit reports only the composite action")]
  public void MoveUnitSubmitReportsOnlyTheCompositeAction()
  {
    var start = new Vector3I(0, 0, 0);
    var mid = new Vector3I(1, 0, 0);
    var end = new Vector3I(2, 0, 0);
    var (session, executor, _, unit) = StartSoloBattle(new Vector3I(4, 1, 4), start, actionPoints: 5);

    var action = new MoveUnit(unit.State, [session.Board.At(mid), session.Board.At(end)]);
    IReadOnlyList<BattleActionResult> results = executor.Submit(action);

    BattleActionResult result = results.RequireSingleResult();
    Assert.True(result.Succeeded);
    Assert.Equal(action, result.Action);
    Assert.True(result.Action.IsDone());
    Assert.Equal(end, session.GetUnitPosition(unit.State).RequireSome().Raw);
    Assert.True(action.IsDone());
  }

  [TestCase(TestName = "MoveUnit reports composite failure when an internal step fails")]
  public void MoveUnitReportsCompositeFailureWhenAnInternalStepFails()
  {
    var faction = BattleTestFactory.MakeFaction("Player");
    var enemyFaction = BattleTestFactory.MakeFaction("Enemy");
    var session = BattleTestFactory.MakeSession(new Vector3I(4, 1, 4), [faction, enemyFaction]);
    var start = new Vector3I(0, 0, 0);
    var mid = new Vector3I(1, 0, 0);
    var end = new Vector3I(2, 0, 0);
    var unit = SpawnUnit(session, BattleTestFactory.MakeCombatant("Alpha", faction, actionPoints: 5), start);
    StartBattle(session);

    var action = new MoveUnit(unit.State, [session.Board.At(mid), session.Board.At(end)]);
    var executor = new BattleActionExecutor(session);
    session.RegisterHook<TileOccupiedBattleEvent>(
      new SpawnUnitOnTileOccupiedHook(mid, end, enemyFaction), HookPhase.After);

    IReadOnlyList<BattleActionResult> results = executor.Submit(action);

    Assert.Equal(2, results.Count);
    Assert.True(results[0].Succeeded);
    Assert.True(results[0].Action is SpawnUnit);
    Assert.False(results[1].Succeeded);
    Assert.Equal(action, results[1].Action);
    Assert.Equal(mid, session.GetUnitPosition(unit.State).RequireSome().Raw);
    Assert.True(action.IsDone());
  }

  [TestCase(TestName = "MoveUnit stops when reaction response makes mover unavailable")]
  public void MoveUnitStopsWhenReactionResponseMakesMoverUnavailable()
  {
    var faction = BattleTestFactory.MakeFaction("Player");
    var session = BattleTestFactory.MakeSession(new Vector3I(4, 1, 4), [faction]);
    var start = new Vector3I(0, 0, 0);
    var mid = new Vector3I(1, 0, 0);
    var end = new Vector3I(2, 0, 0);
    var unit = SpawnUnit(session, BattleTestFactory.MakeCombatant("Alpha", faction, actionPoints: 5), start);
    SpawnUnit(session, BattleTestFactory.MakeCombatant("Bravo", faction, actionPoints: 5), new Vector3I(0, 0, 1));
    StartBattle(session);

    var action = new MoveUnit(unit.State, [session.Board.At(mid), session.Board.At(end)]);
    var executor = new BattleActionExecutor(session);
    session.RegisterHook<TileOccupiedBattleEvent>(
      new PassUnitOnTileOccupiedHook(mid, unit.State), HookPhase.After);

    IReadOnlyList<BattleActionResult> results = executor.Submit(action);

    Assert.Equal(2, results.Count);
    Assert.True(results[0].Succeeded);
    Assert.True(results[0].Action is PassUnit);
    Assert.False(results[1].Succeeded);
    Assert.Equal(action, results[1].Action);
    Assert.Equal(mid, session.GetUnitPosition(unit.State).RequireSome().Raw);
    Assert.True(action.IsDone());
  }

  [TestCase(TestName = "MoveUnit cancels when reaction response kills mover")]
  public void MoveUnitCancelsWhenReactionResponseKillsMover()
  {
    var start = new Vector3I(0, 0, 0);
    var mid = new Vector3I(1, 0, 0);
    var end = new Vector3I(2, 0, 0);
    var (session, executor, _, unit) = StartSoloBattle(new Vector3I(4, 1, 4), start, health: 3, actionPoints: 5);

    var action = new MoveUnit(unit.State, [session.Board.At(mid), session.Board.At(end)]);
    session.RegisterHook<TileOccupiedBattleEvent>(new DamageOnTileOccupiedHook<TileOccupiedBattleEvent>(mid, unit.State, 3, oneShot: true), HookPhase.After);
    IReadOnlyList<BattleActionResult> results = executor.Submit(action);

    BattleActionResult result = results.RequireSingleResult();
    Assert.True(result.Succeeded);
    Assert.True(result.Action is ApplyDamage);
    Assert.True(action.IsDone());
    Assert.Equal(0, unit.State.CurrentHealth);
  }

  [TestCase(TestName = "MoveUnit fails malformed route before moving")]
  public void MoveUnitFailsMalformedRouteBeforeMoving()
  {
    var start = new Vector3I(0, 0, 0);
    var mid = new Vector3I(1, 0, 0);
    var nonAdjacent = new Vector3I(3, 0, 0);
    var (session, executor, _, unit) = StartSoloBattle(new Vector3I(4, 1, 4), start, actionPoints: 5);

    var action = new MoveUnit(unit.State, [session.Board.At(mid), session.Board.At(nonAdjacent)]);
    var result = executor.Submit(action);

    Assert.Equal(0, result.Count);
    Assert.True(action.IsDone());
    Assert.Equal(start, session.GetUnitPosition(unit.State).RequireSome().Raw);
    Assert.Equal(5, unit.State.CurrentActionPoints);
  }

  [TestCase(TestName = "MoveUnit fails empty destinations before moving")]
  public void MoveUnitFailsEmptyDestinationsBeforeMoving()
  {
    var start = new Vector3I(0, 0, 0);
    var (session, executor, _, unit) = StartSoloBattle(new Vector3I(3, 1, 3), start, actionPoints: 5);

    var action = new MoveUnit(unit.State, []);
    var result = executor.Submit(action);

    Assert.Equal(0, result.Count);
    Assert.True(action.IsDone());
    Assert.Equal(start, session.GetUnitPosition(unit.State).RequireSome().Raw);
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

    var recorder = new BattleEventRecorder(session);

    var executor = new BattleActionExecutor(session);
    BattleActionResult result = executor.Submit(BattleAction.EndFactionTurn(factionA)).RequireSingleResult();

    Assert.True(result.Succeeded);
    Assert.True(recorder.OfType<TurnEndedBattleEvent>().Any(battleEvent =>
      battleEvent.Faction == factionA &&
      battleEvent.TurnNumber == 1));
    Assert.True(recorder.OfType<TurnStartedBattleEvent>().Any(battleEvent =>
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
    BattleActionResult first = executor.Submit(BattleAction.MoveUnit(unitA.State, [session.Board.At(0, 0, 1)])).RequireSingleResult();
    BattleActionResult second = executor.Submit(BattleAction.EndFactionTurn(factionA)).RequireSingleResult();

    Assert.True(first.Succeeded);
    Assert.True(first.Action is MoveUnit);
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
    BattleActionResult result = executor.Submit(BattleAction.PassUnit(unitA.State)).RequireSingleResult();

    Assert.True(result.Succeeded);
    Assert.False(Query(session, new IsUnitStillAvailableThisTurn(unitA)));
    Assert.False(Query(session, new CanUnitActNow(unitA)));
    Assert.True(Query(session, new IsUnitStillAvailableThisTurn(unitB)));
    Assert.Equal(faction, session.ActiveSide);
  }

  [TestCase(TestName = "Executor emits action complete events")]
  public void ExecutorEmitsActionCompleteEvents()
  {
    var (session, executor, _, unit) = StartSoloBattle(new Vector3I(4, 1, 4), new Vector3I(1, 0, 1));
    Option<BattleActionResult> resolvedResult = None;
    executor.OnActionComplete += result => resolvedResult = Some(result);

    executor.Submit(BattleAction.MoveUnit(unit.State, [session.Board.At(1, 0, 2)]));

    Assert.True(resolvedResult.IsSome);
    Assert.True(resolvedResult.RequireSome().Succeeded);
    Assert.True(resolvedResult.RequireSome().Action is MoveUnit);
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
    unitA.AddInventoryItem(grenade.Item);
    StartBattle(session);

    var executor = new BattleActionExecutor(session);
    BattleActionResult[] results =
    [
      executor.Submit(BattleAction.MoveUnit(unitA.State, [session.Board.At(0, 0, 1)])).RequireSingleResult(),
      executor.Submit(BattleAction.ThrowItem(unitA.State, grenade, session.Board.At(2, 0, 1))).RequireSingleResult(),
      executor.Submit(BattleAction.EndFactionTurn(factionA)).RequireSingleResult(),
    ];

    Assert.Equal(3, results.Length);
    Assert.True(results[0].Succeeded);
    Assert.True(results[1].Succeeded);
    Assert.True(results[2].Succeeded);
    Assert.Equal(factionB, session.ActiveSide);
    Assert.False(unitA.HasInventoryItem(grenade.Item));
  }

  [TestCase(TestName = "Consumable throwable without charges is removed after one throw")]
  public void ConsumableThrowableWithoutChargesIsRemovedAfterOneThrow()
  {
    var (session, executor, _, unit) = StartSoloBattle(new Vector3I(5, 1, 5), new Vector3I(1, 0, 1), actionPoints: 4);
    var throwable = BattleTestFactory.MakeThrowable("Flare");
    unit.AddInventoryItem(throwable.Item);

    var result = executor.Submit(BattleAction.ThrowItem(unit.State, throwable, session.Board.At(3, 0, 1))).RequireSingleResult();

    Assert.True(result.Succeeded);
    Assert.False(unit.HasInventoryItem(throwable.Item));
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
    BattleActionResult first = executor.Submit(BattleAction.PassUnit(unitA.State)).RequireSingleResult();
    BattleActionResult second = executor.Submit(BattleAction.EndFactionTurn(factionA)).RequireSingleResult();

    Assert.True(first.Succeeded);
    Assert.Equal(factionB, session.ActiveSide);
    Assert.False(second.Succeeded);
    Assert.Equal(BattleActionFailureReason.Rejected, second.FailureReason);
    Assert.Equal(factionB, session.ActiveSide);
  }

  [TestCase(TestName = "Executor reports the composite action for both start and completion")]
  public void ExecutorReportsTheCompositeActionForBothStartAndCompletion()
  {
    var (session, executor, _, unit) = StartSoloBattle(new Vector3I(5, 1, 5), new Vector3I(1, 0, 1), actionPoints: 6);
    var startedActions = new List<BattleAction>();
    executor.OnActionStart += startedActions.Add;

    var move = BattleAction.MoveUnit(unit.State, [session.Board.At(1, 0, 2), session.Board.At(1, 0, 3)], 1);
    var result = executor.Submit(move).RequireSingleResult();

    Assert.True(result.Succeeded);
    Assert.Equal(1, startedActions.Count);
    Assert.True(ReferenceEquals(move, startedActions[0]));
    Assert.True(ReferenceEquals(move, result.Action));
  }

  [TestCase(TestName = "Executor recovers when OnActionStart handler throws")]
  public void ExecutorRecoversWhenOnActionStartHandlerThrows()
  {
    var (session, executor, _, unit) = StartSoloBattle(new Vector3I(4, 2, 4), new Vector3I(1, 0, 1), actionPoints: 5);
    bool shouldThrow = true;
    executor.OnActionStart += _ =>
    {
      if (!shouldThrow)
        return;

      shouldThrow = false;
      throw new InvalidOperationException("boom");
    };

    BattleActionResult first = executor.Submit(BattleAction.MoveUnit(unit.State, [session.Board.At(1, 0, 2)], 2)).RequireSingleResult();
    BattleActionResult second = executor.Submit(BattleAction.MoveUnit(unit.State, [session.Board.At(1, 1, 1)], 2)).RequireSingleResult();

    Assert.False(first.Succeeded);
    Assert.Equal(BattleActionFailureReason.UnexpectedError, first.FailureReason);

    Assert.True(second.Succeeded);
    Assert.Equal(new Vector3I(1, 1, 1), session.GetUnitPosition(unit.State).RequireSome().Raw);
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

  private sealed partial class DamageOnTileOccupiedHook<TEventKey> : BattleHook
    where TEventKey : BattleEventTag
  {
    private readonly Vector3I _position;
    private readonly BattleUnitState _targetUnit;
    private readonly int _damage;
    private readonly bool _oneShot;

    public Option<Vector3I> ObservedTargetPositionDuringEvaluation { get; private set; }

    public DamageOnTileOccupiedHook(Vector3I position, BattleUnitState targetUnit, int damage, bool oneShot)
    {
      _position = position;
      _targetUnit = targetUnit;
      _damage = damage;
      _oneShot = oneShot;
    }

    public override IReadOnlyList<BattleAction> OnEvent(HookContext context, BattleEvent battleEvent)
    {
      if (battleEvent is not IPositionedBattleEvent positioned || positioned.Position.Raw != _position)
        return [];

      ObservedTargetPositionDuringEvaluation = context.Session.GetUnitPosition(_targetUnit)
        .Match(point => Some(point.Raw), () => None);

      if (_oneShot)
        context.Session.UnregisterHook<TEventKey>(this, context.Phase);

      return [BattleAction.ApplyDamage(_targetUnit, _damage)];
    }
  }

  private sealed partial class SpawnUnitOnTileOccupiedHook : BattleHook
  {
    private readonly Vector3I _triggerPosition;
    private readonly Vector3I _spawnPosition;
    private readonly Faction _faction;

    public SpawnUnitOnTileOccupiedHook(Vector3I triggerPosition, Vector3I spawnPosition, Faction faction)
    {
      _triggerPosition = triggerPosition;
      _spawnPosition = spawnPosition;
      _faction = faction;
    }

    public override IReadOnlyList<BattleAction> OnEvent(HookContext context, BattleEvent battleEvent)
    {
      if (battleEvent is not IPositionedBattleEvent positioned || positioned.Position.Raw != _triggerPosition)
        return [];

      context.Session.UnregisterHook<TileOccupiedBattleEvent>(this, context.Phase);
      return [BattleAction.SpawnUnit(BattleTestFactory.MakeCombatant("Blocker", _faction), context.Session.Board.At(_spawnPosition))];
    }
  }

  private sealed partial class PassUnitOnTileOccupiedHook : BattleHook
  {
    private readonly Vector3I _triggerPosition;
    private readonly BattleUnitState _unit;

    public PassUnitOnTileOccupiedHook(Vector3I triggerPosition, BattleUnitState unit)
    {
      _triggerPosition = triggerPosition;
      _unit = unit;
    }

    public override IReadOnlyList<BattleAction> OnEvent(HookContext context, BattleEvent battleEvent)
    {
      if (battleEvent is not IPositionedBattleEvent positioned || positioned.Position.Raw != _triggerPosition)
        return [];

      context.Session.UnregisterHook<TileOccupiedBattleEvent>(this, context.Phase);
      return [BattleAction.PassUnit(_unit)];
    }
  }

  private sealed partial class OneShotRecordingHook : BattleHook
  {
    private readonly Vector3I _position;
    private readonly List<string> _log;
    private readonly string _message;

    public OneShotRecordingHook(Vector3I position, List<string> log, string message)
    {
      _position = position;
      _log = log;
      _message = message;
    }

    public override IReadOnlyList<BattleAction> OnEvent(HookContext context, BattleEvent battleEvent)
    {
      if (battleEvent is not IPositionedBattleEvent positioned || positioned.Position.Raw != _position)
        return [];

      _log.Add(_message);
      context.Session.UnregisterHook<UnitMovedBattleEvent>(this, context.Phase);
      return [];
    }
  }

  private sealed class NonProducingBattleAction : BattleAction
  {
    public NonProducingBattleAction()
      : base("non_producing_test_action")
    {
    }

    internal override Option<BattleAction> NextAction(BattleSession session)
    {
      return None;
    }

    internal override void ConsumeResult(BattleActionResult result)
    {
    }
  }
}
