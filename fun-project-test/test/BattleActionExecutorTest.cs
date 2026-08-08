using FunProject.Battle;
using FunProject.Combatants;
using GdUnit4;
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

[TestSuite]
[RequireGodotRuntime]
public partial class BattleActionExecutorTest
{
  [TestCase(TestName = "Submit resolves a hook's interrupt within the same submission")]
  public void SubmitResolvesInterruptsWithinTheSameSubmission()
  {
    var (session, executor, _, unit) = StartSoloBattle(new Vector3I(3, 1, 3), new Vector3I(0, 0, 0), health: 10, actionPoints: 5);
    var targetPosition = new Vector3I(1, 0, 0);
    executor.RegisterHook<TileOccupiedBattleEvent>(new DamageOnTileOccupiedHook<TileOccupiedBattleEvent>(targetPosition, unit.State, 3, oneShot: true));

    BattleAction submittedAction = BattleAction.MoveUnit(unit.AliveIn(session), [session.Board.At(targetPosition)]);
    BattleActionExecResult result = executor.Submit(submittedAction);

    Assert.True(ReferenceEquals(submittedAction, result.Action));
    Assert.True(HasEvent<UnitMovedBattleEvent>(result));
    Assert.True(HasEvent<UnitDamagedBattleEvent>(result));
    Assert.Equal(targetPosition, session.GetUnitPosition(unit.State).RequireSome().Raw);
    Assert.Equal(7, unit.State.CurrentHealth);
  }

  [TestCase(TestName = "A stale interrupt on an already-dead target is interrupted, not thrown")]
  public void StaleInterruptOnDeadTargetIsInterruptedNotThrown()
  {
    var factionA = BattleTestFactory.MakeFaction("Alpha");
    var factionB = BattleTestFactory.MakeFaction("Bravo");
    var session = BattleTestFactory.MakeSession(new Vector3I(3, 1, 3), [factionA, factionB]);
    var mover = SpawnUnit(session, BattleTestFactory.MakeCombatant("Mover", factionA, health: 20, actionPoints: 5), new Vector3I(0, 0, 0));
    var victim = SpawnUnit(session, BattleTestFactory.MakeCombatant("Victim", factionB, health: 10), new Vector3I(2, 0, 0));
    StartBattle(session);
    var executor = ExecutorFor(session);
    executor.RegisterHook<UnitMovedBattleEvent>(new InterruptActionsHook(
      context =>
      [
        BattleAction.ApplyDamage(context.Session.TryGetAlive(victim.State).RequireSome(), 10),
        BattleAction.ApplyDamage(context.Session.TryGetAlive(victim.State).RequireSome(), 5),
      ]));

    // The first interrupt kills the victim; the second was constructed against a proof
    // that is now stale and must be interrupted quietly rather than throw out of Submit.
    executor.Submit(BattleAction.MoveUnit(mover.AliveIn(session), [session.Board.At(0, 0, 1)]));

    Assert.True(victim.State.IsDead);
  }

  [TestCase(TestName = "Interrupts whose actor died earlier in the submission are interrupted")]
  public void InterruptWithDeadActorIsInterrupted()
  {
    var (session, executor, _, unit) = StartSoloBattle(new Vector3I(3, 1, 3), new Vector3I(0, 0, 0), health: 10, actionPoints: 5);
    var usable = BattleTestFactory.MakeUsableItem("Medkit", maxCharges: 2);
    unit.AddInventoryItem(usable.Item);
    var recorder = new BattleEventRecorder(session);
    executor.RegisterHook<UnitMovedBattleEvent>(new InterruptActionsHook(
      context =>
      [
        BattleAction.ApplyDamage(context.Session.TryGetAlive(unit.State).RequireSome(), 10),
        BattleAction.UseItem(context.Session.TryGetAlive(unit.State).RequireSome(), usable),
      ]));

    // The first interrupt kills the mover; its own queued UseItem must interrupt without
    // spending a charge, AP, or raising ItemUsed.
    executor.Submit(BattleAction.MoveUnit(unit.AliveIn(session), [session.Board.At(1, 0, 0)]));

    Assert.True(unit.State.IsDead);
    Assert.Equal(2, usable.Capability.Current);
    Assert.False(recorder.OfType<ItemUsedBattleEvent>().Any());
  }

  [TestCase(TestName = "A failed submission is unwound; the next Submit does not resume stale work")]
  public void FailedSubmissionIsUnwoundForNextSubmit()
  {
    var (session, executor, faction, unit) = StartSoloBattle(new Vector3I(3, 1, 3), new Vector3I(0, 0, 0));
    executor.RegisterHook<TurnEndedBattleEvent>(new ThrowOnFirstTurnEndedHook());

    Assert.Throws<InvalidOperationException>(
      () => executor.Submit(BattleAction.EndFactionTurn(faction)));

    // The failed submission must leave nothing queued: the recovery submission advances the
    // round exactly once (a stale queued EndFactionTurn would advance it twice).
    executor.Submit(BattleAction.EndFactionTurn(faction));

    Assert.Equal(2, session.TurnNumber);
  }

  [TestCase(TestName = "Submit reports the submitted action once and resolves interrupts between its steps")]
  public void SubmitReportsSubmittedActionAndResolvesInterruptsBetweenSteps()
  {
    var start = new Vector3I(0, 0, 0);
    var mid = new Vector3I(1, 0, 0);
    var end = new Vector3I(2, 0, 0);
    var (session, executor, _, unit) = StartSoloBattle(new Vector3I(4, 1, 4), start, health: 10, actionPoints: 5);
    executor.RegisterHook<TileOccupiedBattleEvent>(new DamageOnTileOccupiedHook<TileOccupiedBattleEvent>(mid, unit.State, 3, oneShot: true));

    BattleAction submittedAction = new MoveUnit(unit.AliveIn(session), [session.Board.At(mid), session.Board.At(end)]);
    BattleActionExecResult results = executor.Submit(submittedAction);

    BattleActionExecResult result = results;
    Assert.True(ReferenceEquals(submittedAction, result.Action));
    Assert.Equal(end, session.GetUnitPosition(unit.State).RequireSome().Raw);
    Assert.Equal(7, unit.State.CurrentHealth);

    // The mid-route interrupt lands between the two committed step moves, and the whole
    // interaction is one result: the composite's steps and the interrupt's events all
    // belong to the single submission.
    int firstMove = IndexOfEvent<UnitMovedBattleEvent>(result, moved => moved.Position.Raw == mid);
    int damage = IndexOfEvent<UnitDamagedBattleEvent>(result, _ => true);
    int secondMove = IndexOfEvent<UnitMovedBattleEvent>(result, moved => moved.Position.Raw == end);
    Assert.True(firstMove < damage && damage < secondMove);
  }

  [TestCase(TestName = "MoveUnit emits movement and tile occupation events after commit")]
  public void MoveUnitEmitsMovementAndTileOccupationEventsAfterCommit()
  {
    var (session, executor, _, unit) = StartSoloBattle(new Vector3I(4, 1, 4), new Vector3I(1, 0, 1), actionPoints: 5);
    var recorder = new BattleEventRecorder(session);

    executor.Submit(BattleAction.MoveUnit(unit.AliveIn(session), [session.Board.At(1, 0, 2)], 2));

    Assert.True(recorder.OfType<UnitMovedBattleEvent>().Any(battleEvent =>
      battleEvent.Position.Raw == new Vector3I(1, 0, 2) &&
      battleEvent.SourcePosition.Raw == new Vector3I(1, 0, 1)));
    Assert.True(recorder.OfType<TileOccupiedBattleEvent>().Any(battleEvent =>
      battleEvent.Position.Raw == new Vector3I(1, 0, 2)));
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

    executor.Submit(BattleAction.MoveUnit(unit.AliveIn(session), [session.Board.At(destination)], 2));

    Assert.Equal(destination, observedPositionDuringEvent.RequireSome());
  }

  [TestCase(TestName = "Executor evaluates matching hooks deterministically")]
  public void ExecutorEvaluatesMatchingHooksDeterministically()
  {
    var (session, executor, _, unit) = StartSoloBattle(new Vector3I(3, 1, 3), new Vector3I(0, 0, 0));

    var log = new List<string>();
    var targetPosition = new Vector3I(1, 0, 0);
    executor.RegisterHook<UnitMovedBattleEvent>(new PositionRecordingHook(targetPosition, log, "late"), priority: 10);
    executor.RegisterHook<UnitMovedBattleEvent>(new PositionRecordingHook(targetPosition, log, "first"));
    executor.RegisterHook<UnitMovedBattleEvent>(new PositionRecordingHook(targetPosition, log, "second"));
    executor.RegisterHook<UnitDamagedBattleEvent>(new PositionRecordingHook(targetPosition, log, "ignored"), priority: -10);

    executor.Submit(BattleAction.MoveUnit(unit.AliveIn(session), [session.Board.At(targetPosition)]));

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
    executor.RegisterHook<UnitDamagedBattleEvent>(ignoredHook);
    executor.RegisterHook<UnitMovedBattleEvent>(matchingHook);

    executor.Submit(BattleAction.MoveUnit(unit.AliveIn(session), [session.Board.At(targetPosition)]));

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
    executor.RegisterHook<IPositionedBattleEvent>(new PositionRecordingHook(targetPosition, log, "matched"));

    executor.Submit(BattleAction.MoveUnit(unit.AliveIn(session), [session.Board.At(targetPosition)]));

    Assert.True(log.SequenceEqual(["matched", "matched"]));
  }

  [TestCase(TestName = "Executor consumes one-shot hooks before later actions")]
  public void ExecutorConsumesOneShotHooksBeforeLaterActions()
  {
    var start = new Vector3I(0, 0, 0);
    var targetPosition = new Vector3I(1, 0, 0);
    var (session, executor, _, unit) = StartSoloBattle(new Vector3I(3, 1, 3), start, actionPoints: 5);
    var log = new List<string>();
    executor.RegisterHook<UnitMovedBattleEvent>(new OneShotRecordingHook(targetPosition, log, "matched"));

    BattleActionExecResult[] results =
    [
      executor.Submit(BattleAction.MoveUnit(unit.AliveIn(session), [session.Board.At(targetPosition)])),
      executor.Submit(BattleAction.MoveUnit(unit.AliveIn(session), [session.Board.At(start)])),
      executor.Submit(BattleAction.MoveUnit(unit.AliveIn(session), [session.Board.At(targetPosition)])),
    ];

    Assert.Equal(3, results.Length);
    Assert.True(log.SequenceEqual(["matched"]));
  }

  [TestCase(TestName = "Executor resolves interrupt actions in hook priority order")]
  public void ExecutorResolvesInterruptActionsInHookPriorityOrder()
  {
    var start = new Vector3I(0, 0, 0);
    var targetPosition = new Vector3I(1, 0, 0);
    var (session, executor, _, unit) = StartSoloBattle(new Vector3I(3, 1, 3), start, health: 10, actionPoints: 5);
    executor.RegisterHook<UnitMovedBattleEvent>(
      new DamageOnTileOccupiedHook<UnitMovedBattleEvent>(targetPosition, unit.State, 2, oneShot: true), priority: 10);
    executor.RegisterHook<UnitMovedBattleEvent>(
      new DamageOnTileOccupiedHook<UnitMovedBattleEvent>(targetPosition, unit.State, 1, oneShot: true), priority: 0);

    var recorder = new BattleEventRecorder(session);
    executor.Submit(BattleAction.MoveUnit(unit.AliveIn(session), [session.Board.At(targetPosition)]));

    int[] damageAmounts = recorder.OfType<UnitDamagedBattleEvent>()
      .Select(damaged => damaged.TotalAmount)
      .ToArray();

    Assert.True(damageAmounts.SequenceEqual([1, 2]));
  }

  [TestCase(TestName = "Executor resolves interrupts across a step's events in commit order")]
  public void ExecutorResolvesInterruptsAcrossEventsInCommitOrder()
  {
    var start = new Vector3I(0, 0, 0);
    var targetPosition = new Vector3I(1, 0, 0);
    var (session, executor, _, unit) = StartSoloBattle(new Vector3I(3, 1, 3), start, health: 10, actionPoints: 5);
    // One move step commits UnitMoved then TileOccupied. A hook on each event pins the
    // aggregate-reverse-once contract: the FIRST event's interrupt resolves first. A per-event
    // reverse would flip this to [2, 1].
    executor.RegisterHook<UnitMovedBattleEvent>(
      new DamageOnTileOccupiedHook<UnitMovedBattleEvent>(targetPosition, unit.State, 1, oneShot: true));
    executor.RegisterHook<TileOccupiedBattleEvent>(
      new DamageOnTileOccupiedHook<TileOccupiedBattleEvent>(targetPosition, unit.State, 2, oneShot: true));

    var recorder = new BattleEventRecorder(session);
    executor.Submit(BattleAction.MoveUnit(unit.AliveIn(session), [session.Board.At(targetPosition)]));

    int[] damageAmounts = recorder.OfType<UnitDamagedBattleEvent>()
      .Select(damaged => damaged.TotalAmount)
      .ToArray();

    Assert.True(damageAmounts.SequenceEqual([1, 2]));
  }

  [TestCase(TestName = "Executor queues reaction response after committed tile occupation")]
  public void ExecutorQueuesReactionResponseAfterCommittedTileOccupation()
  {
    var (session, executor, _, unit) = StartSoloBattle(new Vector3I(3, 1, 3), new Vector3I(0, 0, 0), health: 10, actionPoints: 5);
    var targetPosition = new Vector3I(1, 0, 0);
    var reaction = new DamageOnTileOccupiedHook<TileOccupiedBattleEvent>(targetPosition, unit.State, 3, oneShot: true);
    executor.RegisterHook<TileOccupiedBattleEvent>(reaction);

    executor.Submit(BattleAction.MoveUnit(unit.AliveIn(session), [session.Board.At(targetPosition)]));

    Assert.Equal(targetPosition, session.GetUnitPosition(unit.State).RequireSome().Raw);
    Assert.Equal(targetPosition, reaction.ObservedTargetPositionDuringEvaluation.RequireSome());
    Assert.Equal(7, unit.State.CurrentHealth);
  }

  [TestCase(TestName = "A rejected submission throws without consuming one-shot hooks")]
  public void RejectedSubmissionThrowsWithoutConsumingOneShotHooks()
  {
    var (session, executor, _, unit) = StartSoloBattle(new Vector3I(4, 1, 4), new Vector3I(0, 0, 0), health: 10);
    var targetPosition = new Vector3I(2, 0, 0);
    executor.RegisterHook<TileOccupiedBattleEvent>(new DamageOnTileOccupiedHook<TileOccupiedBattleEvent>(targetPosition, unit.State, 3, oneShot: true));

    // (2, 0, 0) is not adjacent to (0, 0, 0): the malformed route rejects, which the
    // executor surfaces as an invariant-break throw.
    Assert.Throws<InvalidOperationException>(
      () => executor.Submit(BattleAction.MoveUnit(unit.AliveIn(session), [session.Board.At(targetPosition)])));
    Assert.Equal(new Vector3I(0, 0, 0), session.GetUnitPosition(unit.State).RequireSome().Raw);
    Assert.Equal(10, unit.State.CurrentHealth);

    // The one-shot hook survived the rejected submission and still fires.
    executor.Submit(BattleAction.MoveUnit(unit.AliveIn(session), [session.Board.At(1, 0, 0)]));
    executor.Submit(BattleAction.MoveUnit(unit.AliveIn(session), [session.Board.At(targetPosition)]));
    Assert.Equal(7, unit.State.CurrentHealth);
  }

  [TestCase(TestName = "Executor supports multiple one-shot hook instances")]
  public void ExecutorSupportsMultipleOneShotHookInstances()
  {
    var start = new Vector3I(0, 0, 0);
    var firstTrap = new Vector3I(1, 0, 0);
    var secondTrap = new Vector3I(2, 0, 0);
    var (session, executor, _, unit) = StartSoloBattle(new Vector3I(4, 1, 4), start, health: 10, actionPoints: 5);
    executor.RegisterHook<TileOccupiedBattleEvent>(
      new DamageOnTileOccupiedHook<TileOccupiedBattleEvent>(firstTrap, unit.State, 2, oneShot: true));
    executor.RegisterHook<TileOccupiedBattleEvent>(
      new DamageOnTileOccupiedHook<TileOccupiedBattleEvent>(secondTrap, unit.State, 3, oneShot: true));

    executor.Submit(BattleAction.MoveUnit(unit.AliveIn(session), [session.Board.At(firstTrap), session.Board.At(secondTrap)]));

    Assert.Equal(5, unit.State.CurrentHealth);
    Assert.Equal(secondTrap, session.GetUnitPosition(unit.State).RequireSome().Raw);
  }

  [TestCase(TestName = "MoveUnit submit reports only the submitted action")]
  public void MoveUnitSubmitReportsOnlyTheSubmittedAction()
  {
    var start = new Vector3I(0, 0, 0);
    var mid = new Vector3I(1, 0, 0);
    var end = new Vector3I(2, 0, 0);
    var (session, executor, _, unit) = StartSoloBattle(new Vector3I(4, 1, 4), start, actionPoints: 5);

    var action = new MoveUnit(unit.AliveIn(session), [session.Board.At(mid), session.Board.At(end)]);
    BattleActionExecResult results = executor.Submit(action);

    BattleActionExecResult result = results;
    Assert.True(ReferenceEquals(action, result.Action));
    Assert.Equal(end, session.GetUnitPosition(unit.State).RequireSome().Raw);
  }

  [TestCase(TestName = "MoveUnit throws when an internal step becomes blocked")]
  public void MoveUnitThrowsWhenAnInternalStepBecomesBlocked()
  {
    var faction = BattleTestFactory.MakeFaction("Player");
    var enemyFaction = BattleTestFactory.MakeFaction("Enemy");
    var session = BattleTestFactory.MakeSession(new Vector3I(4, 1, 4), [faction, enemyFaction]);
    var start = new Vector3I(0, 0, 0);
    var mid = new Vector3I(1, 0, 0);
    var end = new Vector3I(2, 0, 0);
    var unit = SpawnUnit(session, BattleTestFactory.MakeCombatant("Alpha", faction, actionPoints: 5), start);
    StartBattle(session);

    var action = new MoveUnit(unit.AliveIn(session), [session.Board.At(mid), session.Board.At(end)]);
    var executor = ExecutorFor(session);
    executor.RegisterHook<TileOccupiedBattleEvent>(
      new SpawnUnitOnTileOccupiedHook(mid, end, enemyFaction));

    // The blocker spawned by the mid-step interrupt occupies the route's next tile; the
    // blocked step rejects and the executor surfaces the invariant break. The first step
    // stays committed.
    Assert.Throws<InvalidOperationException>(() => executor.Submit(action));
    Assert.Equal(mid, session.GetUnitPosition(unit.State).RequireSome().Raw);
    Assert.True(session.GetUnitAt(session.Board.At(end)).IsSome);
  }

  [TestCase(TestName = "MoveUnit stops quietly when a reaction kills the mover mid route")]
  public void MoveUnitStopsQuietlyWhenAReactionKillsTheMoverMidRoute()
  {
    var start = new Vector3I(0, 0, 0);
    var mid = new Vector3I(1, 0, 0);
    var end = new Vector3I(2, 0, 0);
    var (session, executor, _, unit) = StartSoloBattle(new Vector3I(4, 1, 4), start, health: 3, actionPoints: 5);

    var action = new MoveUnit(unit.AliveIn(session), [session.Board.At(mid), session.Board.At(end)]);
    executor.RegisterHook<TileOccupiedBattleEvent>(new DamageOnTileOccupiedHook<TileOccupiedBattleEvent>(mid, unit.State, 3, oneShot: true));

    BattleActionExecResult results = executor.Submit(action);

    // Death interrupts the remainder of the route without failing the submission.
    BattleActionExecResult result = results;
    Assert.True(ReferenceEquals(action, result.Action));
    Assert.Equal(0, unit.State.CurrentHealth);
    Assert.True(HasEvent<UnitKilledBattleEvent>(result));
  }

  [TestCase(TestName = "MoveUnit fails malformed route before moving")]
  public void MoveUnitFailsMalformedRouteBeforeMoving()
  {
    var start = new Vector3I(0, 0, 0);
    var mid = new Vector3I(1, 0, 0);
    var nonAdjacent = new Vector3I(3, 0, 0);
    var (session, executor, _, unit) = StartSoloBattle(new Vector3I(4, 1, 4), start, actionPoints: 5);

    var action = new MoveUnit(unit.AliveIn(session), [session.Board.At(mid), session.Board.At(nonAdjacent)]);

    Assert.Throws<InvalidOperationException>(() => executor.Submit(action));
    Assert.Equal(start, session.GetUnitPosition(unit.State).RequireSome().Raw);
    Assert.Equal(5, unit.State.CurrentActionPoints);
  }

  [TestCase(TestName = "MoveUnit fails empty destinations before moving")]
  public void MoveUnitFailsEmptyDestinationsBeforeMoving()
  {
    var start = new Vector3I(0, 0, 0);
    var (session, executor, _, unit) = StartSoloBattle(new Vector3I(3, 1, 3), start, actionPoints: 5);

    var action = new MoveUnit(unit.AliveIn(session), []);

    Assert.Throws<InvalidOperationException>(() => executor.Submit(action));
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

    var executor = ExecutorFor(session);
    executor.Submit(BattleAction.EndFactionTurn(factionA));

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

    var executor = ExecutorFor(session);
    BattleActionExecResult first = executor.Submit(BattleAction.MoveUnit(unitA.AliveIn(session), [session.Board.At(0, 0, 1)]));
    BattleActionExecResult second = executor.Submit(BattleAction.EndFactionTurn(factionA));

    Assert.True(first.Action is MoveUnit);
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

    var executor = ExecutorFor(session);
    executor.Submit(BattleAction.PassUnit(unitA.AliveIn(session)));

    Assert.False(Query(session, new IsUnitStillAvailableThisTurn(unitA)));
    Assert.False(Query(session, new CanUnitActNow(unitA)));
    Assert.True(Query(session, new IsUnitStillAvailableThisTurn(unitB)));
    Assert.Equal(faction, session.ActiveSide);
  }

  [TestCase(TestName = "Executor submit returns one result per submitted action")]
  public void ExecutorSubmitReturnsOneResultPerSubmittedAction()
  {
    var factionA = BattleTestFactory.MakeFaction("A");
    var factionB = BattleTestFactory.MakeFaction("B");
    var session = BattleTestFactory.MakeSession(new Vector3I(5, 1, 5), [factionA, factionB]);
    var unitA = SpawnUnit(session, BattleTestFactory.MakeCombatant("A1", factionA, actionPoints: 5), new Vector3I(0, 0, 0));
    SpawnUnit(session, BattleTestFactory.MakeCombatant("B1", factionB), new Vector3I(3, 0, 0));
    var grenade = BattleTestFactory.MakeGrenade("Practice");
    unitA.AddInventoryItem(grenade.Item);
    StartBattle(session);

    var executor = ExecutorFor(session);
    BattleActionExecResult[] results =
    [
      executor.Submit(BattleAction.MoveUnit(unitA.AliveIn(session), [session.Board.At(0, 0, 1)])),
      executor.Submit(BattleAction.ThrowItem(unitA.AliveIn(session), grenade, session.Board.At(2, 0, 1))),
      executor.Submit(BattleAction.EndFactionTurn(factionA)),
    ];

    Assert.Equal(3, results.Length);
    Assert.Equal(factionB, session.ActiveSide);
    Assert.False(unitA.HasInventoryItem(grenade.Item));
  }

  [TestCase(TestName = "Consumable throwable without charges is removed after one throw")]
  public void ConsumableThrowableWithoutChargesIsRemovedAfterOneThrow()
  {
    var (session, executor, _, unit) = StartSoloBattle(new Vector3I(5, 1, 5), new Vector3I(1, 0, 1), actionPoints: 4);
    var throwable = BattleTestFactory.MakeThrowable("Flare");
    unit.AddInventoryItem(throwable.Item);

    executor.Submit(BattleAction.ThrowItem(unit.AliveIn(session), throwable, session.Board.At(3, 0, 1)));

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

    var executor = ExecutorFor(session);
    executor.Submit(BattleAction.PassUnit(unitA.AliveIn(session)));
    Assert.Equal(factionB, session.ActiveSide);

    // Faction A's turn already auto-advanced; the stale submission is an invariant break.
    Assert.Throws<InvalidOperationException>(
      () => executor.Submit(BattleAction.EndFactionTurn(factionA)));
    Assert.Equal(factionB, session.ActiveSide);
  }

  private static bool HasEvent<TEvent>(in BattleActionExecResult result)
    where TEvent : BattleEvent
  {
    foreach (var battleEvent in result.EventsThatOccurred)
      if (battleEvent is TEvent)
        return true;

    return false;
  }

  private static int IndexOfEvent<TEvent>(BattleActionExecResult result, Func<TEvent, bool> predicate)
    where TEvent : BattleEvent
  {
    for (int i = 0; i < result.EventsThatOccurred.Length; i++)
    {
      if (result.EventsThatOccurred[i] is TEvent typed && predicate(typed))
        return i;
    }

    return -1;
  }

  private sealed partial class DamageOnTileOccupiedHook<TEventKey> : BattleHook
    where TEventKey : BattleEventTag
  {
    private readonly Vector3I _position;
    private readonly BattleUnitState _targetUnit;
    private readonly int _damage;
    private readonly bool _oneShot;
    private bool _spent;

    public Option<Vector3I> ObservedTargetPositionDuringEvaluation { get; private set; }

    public DamageOnTileOccupiedHook(Vector3I position, BattleUnitState targetUnit, int damage, bool oneShot)
    {
      _position = position;
      _targetUnit = targetUnit;
      _damage = damage;
      _oneShot = oneShot;
    }

    public override bool NeedsToUnregister => _spent;

    public override IReadOnlyList<BattleAction> OnEvent(HookContext context, BattleEvent battleEvent)
    {
      if (battleEvent is not IPositionedBattleEvent positioned || positioned.Position.Raw != _position)
        return [];

      ObservedTargetPositionDuringEvaluation = context.Session.GetUnitPosition(_targetUnit)
        .Match(point => Some(point.Raw), () => None);

      if (_oneShot)
        _spent = true;

      System.Collections.Generic.List<BattleAction> interrupts = [];
      context.Session.TryGetAlive(_targetUnit)
        .IfSome(alive => interrupts.Add(BattleAction.ApplyDamage(alive, _damage)));
      return interrupts;
    }
  }

  private sealed partial class SpawnUnitOnTileOccupiedHook : BattleHook
  {
    private readonly Vector3I _triggerPosition;
    private readonly Vector3I _spawnPosition;
    private readonly Faction _faction;
    private bool _spent;

    public SpawnUnitOnTileOccupiedHook(Vector3I triggerPosition, Vector3I spawnPosition, Faction faction)
    {
      _triggerPosition = triggerPosition;
      _spawnPosition = spawnPosition;
      _faction = faction;
    }

    public override bool NeedsToUnregister => _spent;

    public override IReadOnlyList<BattleAction> OnEvent(HookContext context, BattleEvent battleEvent)
    {
      if (battleEvent is not IPositionedBattleEvent positioned || positioned.Position.Raw != _triggerPosition)
        return [];

      _spent = true;
      return [BattleAction.SpawnUnit(BattleTestFactory.MakeCombatant("Blocker", _faction), context.Session.Board.At(_spawnPosition))];
    }
  }

  private sealed partial class OneShotRecordingHook : BattleHook
  {
    private readonly Vector3I _position;
    private readonly List<string> _log;
    private readonly string _message;
    private bool _spent;

    public OneShotRecordingHook(Vector3I position, List<string> log, string message)
    {
      _position = position;
      _log = log;
      _message = message;
    }

    public override bool NeedsToUnregister => _spent;

    public override IReadOnlyList<BattleAction> OnEvent(HookContext context, BattleEvent battleEvent)
    {
      if (battleEvent is not IPositionedBattleEvent positioned || positioned.Position.Raw != _position)
        return [];

      _log.Add(_message);
      _spent = true;
      return [];
    }
  }

  // Returns interrupts built at firing time by a delegate, so tests can construct
  // proof-carrying actions against live session state mid-dispatch.
  private sealed class InterruptActionsHook(Func<HookContext, IReadOnlyList<BattleAction>> build) : BattleHook<UnitMovedBattleEvent>
  {
    protected override IReadOnlyList<BattleAction> OnEvent(HookContext context, UnitMovedBattleEvent evt)
      => build(context);
  }

  // Throws on its first firing only, so a recovery submission can succeed afterwards.
  private sealed class ThrowOnFirstTurnEndedHook : BattleHook<TurnEndedBattleEvent>
  {
    private bool _thrown;

    protected override IReadOnlyList<BattleAction> OnEvent(HookContext context, TurnEndedBattleEvent evt)
    {
      if (_thrown)
        return [];
      _thrown = true;
      throw new InvalidOperationException("Hook failure.");
    }
  }
}
