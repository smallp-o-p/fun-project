using FunProject.Battle;
using FunProject.Combatants;
using FunProject.Stats;
using FunProject.Weapons;
using GdUnit4;
using Godot;
using System;
using System.Collections.Generic;

[TestSuite]
[RequireGodotRuntime]
public partial class BattleActionExecutorTest
{
  [TestCase(false, true, TestName = "Submit resolves a hook's interrupt within the same submission")]
  [TestCase(true, true, TestName = "Submit reports the submitted action once and resolves interrupts between its steps")]
  [TestCase(true, false, TestName = "MoveUnit submit reports only the submitted action")]
  public void SubmitReportsMovementAndInterrupts(bool twoSteps, bool reaction)
  {
    var start = new Vector3I(0, 0, 0);
    var mid = new Vector3I(1, 0, 0);
    var end = new Vector3I(2, 0, 0);
    using var battle = BattleFixture.Solo(twoSteps ? new Vector3I(4, 1, 4) : new Vector3I(3, 1, 3), start,
      health: reaction ? 10 : 20, actionPoints: 5);
    var unit = battle.Unit;
    var reactionHook = new DamageOnTileOccupiedHook<TileOccupiedBattleEvent>(mid, unit, 3, oneShot: true);
    if (reaction)
      battle.RegisterHook<TileOccupiedBattleEvent>(reactionHook);

    BattleBoardState.ValidatedPoint[] route = twoSteps ? [battle.At(mid), battle.At(end)] : [battle.At(mid)];
    BattleAction submittedAction = BattleAction.MoveUnit(battle.Alive(unit), route);
    BattleActionExecResult result = battle.Submit(submittedAction).RequireSome();

    Assert.True(ReferenceEquals(submittedAction, result.Action));
    Assert.Equal(twoSteps ? end : mid, battle.PositionOf(unit).RequireSome().Raw);
    if (!reaction)
      return;

    // The hook's interrupt lands inside the same submission: movement and damage commit as
    // one result, the reaction observes the unit on the just-committed mid tile, and on a
    // two-step route the interrupt resolves between the two committed step moves.
    var events = result.EventsThatOccurred.ToArray();
    Assert.True(events.EventsOf<UnitMovedBattleEvent>().Length > 0);
    Assert.True(events.EventsOf<UnitDamagedBattleEvent>().Length > 0);
    Assert.Equal(7, unit.CurrentHealth);
    Assert.Equal(mid, reactionHook.ObservedTargetPositionDuringEvaluation.RequireSome());
    if (twoSteps)
    {
      int firstMove = events.EventIndex<UnitMovedBattleEvent>(moved => moved.Position.Raw == mid);
      int damage = events.EventIndex<UnitDamagedBattleEvent>();
      int secondMove = events.EventIndex<UnitMovedBattleEvent>(moved => moved.Position.Raw == end);
      Assert.True(firstMove < damage && damage < secondMove);
    }
  }

  [TestCase(TestName = "A stale interrupt on an already-dead target is interrupted, not thrown")]
  public void StaleInterruptOnDeadTargetIsInterruptedNotThrown()
  {
    var factionA = TestData.MakeFaction("Alpha");
    var factionB = TestData.MakeFaction("Bravo");
    using var battle = new BattleFixture(new Vector3I(3, 1, 3), [factionA, factionB]);
    var mover = battle.Spawn(TestData.MakeCombatant("Mover", factionA, health: 20, actionPoints: 5), new Vector3I(0, 0, 0));
    var victim = battle.Spawn(TestData.MakeCombatant("Victim", factionB, health: 10), new Vector3I(2, 0, 0));
    battle.Start();
    battle.RegisterHook<UnitMovedBattleEvent>(new InterruptActionsHook(
      context =>
      [
        BattleAction.ApplyDamage(context.Read.State.TryGetAlive(victim).RequireSome(), 10),
        BattleAction.ApplyDamage(context.Read.State.TryGetAlive(victim).RequireSome(), 5),
      ]));

    // The first interrupt kills the victim; the second was constructed against a proof
    // that is now stale and must be interrupted quietly rather than throw out of Submit.
    battle.Submit(BattleAction.MoveUnit(battle.Alive(mover), [battle.At(0, 0, 1)]));

    Assert.True(victim.IsDead);
  }

  [TestCase(TestName = "Interrupts whose actor died earlier in the submission are interrupted")]
  public void InterruptWithDeadActorIsInterrupted()
  {
    using var battle = BattleFixture.Solo(new Vector3I(3, 1, 3), new Vector3I(0, 0, 0), health: 10, actionPoints: 5, start: false);
    var unit = battle.Unit;
    var usable = TestData.MakeUsableItem("Medkit", maxCharges: 2);
    unit.AddInventoryItem(usable.Item);
    battle.Start();
    battle.ClearEvents();
    battle.RegisterHook<UnitMovedBattleEvent>(new InterruptActionsHook(
      context =>
      [
        BattleAction.ApplyDamage(context.Read.State.TryGetAlive(unit).RequireSome(), 10),
        BattleAction.UseItem(context.Read.State.TryGetAlive(unit).RequireSome(), usable),
      ]));

    // The first interrupt kills the mover; its own queued UseItem must interrupt without
    // spending a charge, AP, or raising ItemUsed.
    battle.Submit(BattleAction.MoveUnit(battle.Alive(unit), [battle.At(1, 0, 0)]));

    Assert.True(unit.IsDead);
    Assert.Equal(2, usable.Capability.Current);
    Assert.False(battle.Events.EventsOf<ItemUsedBattleEvent>().AsValueEnumerable().Any());
  }

  [TestCase(TestName = "A failed submission is unwound; the next Submit does not resume stale work")]
  public void FailedSubmissionIsUnwoundForNextSubmit()
  {
    using var battle = BattleFixture.Solo(new Vector3I(4, 1, 4), new Vector3I(0, 0, 0), actionPoints: 5);
    var unit = battle.Unit;
    var runtime = battle.Runtime;
    var expected = new InvalidOperationException("route fault");
    battle.RegisterHook<UnitMovedBattleEvent>(new InterruptActionsHook(
      context => [BattleAction.PassUnit(context.Read.State.TryGetAlive(unit).RequireSome())]));
    battle.RegisterHook<TileOccupiedBattleEvent>(new ThrowOnFirstEventHook(expected));
    IReadOnlyList<UnitAction> retainedOptions = battle.Query(
      new GetAvailableActionsForUnit(battle.Alive(unit)));
    foreach (UnitAction option in retainedOptions)
      _ = option.IsAvailable;
    int completedCount = 0;
    runtime.ActionCompleted += _ => completedCount++;
    battle.ClearEvents();

    var caught = Assert.Throws<InvalidOperationException>(() =>
      battle.Submit(BattleAction.MoveUnit(battle.Alive(unit), [battle.At(1, 0, 0), battle.At(2, 0, 0)])));

    // The original fault surfaces unchanged and never turns into a completion.
    Assert.True(ReferenceEquals(expected, caught));
    Assert.Equal(0, completedCount);
    // The first tile's committed mutations survive; the queued interrupt, the remaining
    // route, and any undispatched follow-up do not run.
    Assert.Equal(new Vector3I(1, 0, 0), battle.PositionOf(unit).RequireSome().Raw);
    Assert.Equal(4, unit.CurrentActionPoints);
    Assert.Equal(0, battle.Events.EventsOf<UnitActivationEndedBattleEvent>().Length);
    // Every retained option leaves the failed submission dirty, even the ones the events
    // alone would not have touched.
    Assert.True(retainedOptions.AsValueEnumerable()
      .Single(option => option.Action is EndTurnActionDefinition).IsDirty);

    // The recovery submission starts from a clean slate: one activation ends, the round
    // advances exactly once, and no stale route step executes.
    battle.ClearEvents();
    battle.Pass(unit);
    Assert.Equal(new Vector3I(1, 0, 0), battle.PositionOf(unit).RequireSome().Raw);
    Assert.Equal(1, battle.Events.EventsOf<UnitActivationEndedBattleEvent>().Length);
    Assert.Equal(2, battle.Query(new GetCurrentTurnQuery()).RequireSome().RoundNumber);
  }

  [TestCase(TestName = "A terminal request followed by a hook fault closes combat from retained state without success events")]
  public void TerminalRequestThenHookFaultClosesWithoutSuccessEvents()
  {
    var start = new Vector3I(0, 0, 0);
    var mid = new Vector3I(1, 0, 0);
    var end = new Vector3I(2, 0, 0);
    using var battle = BattleFixture.Solo(new Vector3I(4, 1, 4), start, actionPoints: 5);
    var unit = battle.Unit;
    var runtime = battle.Runtime;
    var expected = new InvalidOperationException("terminal route fault");
    battle.RegisterHook<UnitMovedBattleEvent>(new RequestVictoryOnMovedHook());
    battle.RegisterHook<TileOccupiedBattleEvent>(new ThrowOnFirstEventHook(expected));
    int completedCount = 0;
    runtime.ActionCompleted += _ => completedCount++;

    var caught = Assert.Throws<InvalidOperationException>(() =>
      battle.Submit(BattleAction.MoveUnit(battle.Alive(unit), [battle.At(mid), battle.At(end)])));

    Assert.True(ReferenceEquals(expected, caught));
    Assert.Equal(mid, battle.Alive(unit).Position.Raw);
    Assert.Equal(0, completedCount);
    Assert.Equal(0, battle.Events.EventsOf<SessionEndedBattleEvent>().Length);
    Assert.True(battle.Query(new GetCompletedBattleQuery()).IsSome);
    Assert.True(battle.Submit(BattleAction.EndFactionTurn(battle.PlayerFaction)).IsNone);
    // The frozen report reflects the actual retained state: the mover survived the first
    // tile unwounded, so nobody is wounded and the roster is the one combatant.
    CompletedBattle completed = battle.Query(new GetCompletedBattleQuery()).RequireSome();
    Assert.Equal(BattleOutcome.Victory, completed.Outcome);
    Assert.Equal(1, completed.Factions[battle.PlayerFaction].Spawned);
    Assert.Equal(0, completed.Factions[battle.PlayerFaction].Killed);
    Assert.Equal(0, completed.FactionSummaries[battle.PlayerFaction].CombatantsWounded.Count);
  }

  [TestCase(TestName = "A fault during end-event notification keeps the installed snapshot and propagates the cause")]
  public void EndNotificationFaultKeepsInstalledSnapshotAvailable()
  {
    using var battle = BattleFixture.Duel();
    battle.ApplyDamage(battle.PlayerUnit, 20, DamageKind.Stun);
    battle.ApplyDamage(battle.EnemyUnit, 20, DamageKind.Stun);
    var expected = new InvalidOperationException("end notification fault");
    CompletedBattle? callbackSnapshot = null;
    int liveMaxAtEndCallback = 0;
    battle.RegisterHook<SessionEndedBattleEvent>(new CampaignMutationFaultOnSessionEndedHook(
      battle, battle.PlayerUnit, expected, (snapshot, liveMax) =>
      {
        callbackSnapshot = snapshot;
        liveMaxAtEndCallback = liveMax;
      }));
    int completedCount = 0;
    battle.Runtime.ActionCompleted += _ => completedCount++;

    var caught = Assert.Throws<InvalidOperationException>(() =>
      battle.EndFactionTurn(battle.PlayerFaction));

    Assert.True(ReferenceEquals(expected, caught));
    Assert.Equal(0, completedCount);
    // The real end callback proved the campaign mutation was live before it threw.
    Assert.Equal(40, liveMaxAtEndCallback);
    // The snapshot installed before the broadcast stays authoritative: the failed
    // notification never recaptures, so the same reference survives with its original
    // frozen numbers even though the campaign health stat now reads 40.
    CompletedBattle snapshot = battle.Query(new GetCompletedBattleQuery()).RequireSome();
    Assert.True(ReferenceEquals(callbackSnapshot, snapshot));
    Assert.Equal(20,
      snapshot.FactionSummaries[battle.PlayerFaction]
        .HealthByCombatant[battle.PlayerUnit.Combatant].MaxHealth);
    Assert.Equal(BattleOutcome.Draw, snapshot.Outcome);
    Assert.True(battle.Query(new GetCurrentTurnQuery()).IsNone);
    Assert.True(battle.Submit(BattleAction.EndFactionTurn(battle.PlayerFaction)).IsNone);
  }

  [TestCase(TestName = "A capture fault during unwind keeps the primary exception, retains the cause, and closes the runtime")]
  public void SecondaryCaptureFailureRetainsCauseAndDisposesRuntime()
  {
    // The loadout StatMod is the supported TargetType seam: armed inside the hook right
    // after the terminal request, its TargetType read faults the frozen report's effective
    // stat resolution, so the unwind's real CompletedBattle.Capture is what fails — no
    // production seam and no replacement capture function involved.
    var armedMod = new ArmedFaultStatMod(new InvalidOperationException("capture stat fault"));
    var player = TestData.MakeFaction("Player");
    var enemy = TestData.MakeFaction("Enemy");
    using var battle = new BattleFixture(new Vector3I(4, 1, 4), [player, enemy]);
    var unit = battle.Spawn(
      TestData.MakeCombatant("Alpha", player, actionPoints: 5), new Vector3I(0, 0, 0), statMods: [armedMod]);
    battle.Spawn(TestData.MakeCombatant("Hostile", enemy, health: 100), new Vector3I(3, 0, 3));
    battle.Start();
    var runtime = battle.Runtime;
    var expected = new InvalidOperationException("primary hook fault");
    Exception captureCause = armedMod.Fault;
    battle.RegisterHook<UnitMovedBattleEvent>(
      new RequestEndArmThrowHook(armedMod, expected));
    int completedCount = 0;
    runtime.ActionCompleted += _ => completedCount++;

    var caught = Assert.Throws<InvalidOperationException>(() =>
      battle.Submit(BattleAction.MoveUnit(battle.Alive(unit), [battle.At(1, 0, 0)])));

    // The primary exception keeps its identity; the actual capture cause rides on it.
    Assert.True(ReferenceEquals(expected, caught));
    var data = caught.Data;
    const string captureFailureKey = BattleActionExecutor.BattleCompletionCaptureFailureDataKey;
    Assert.True(data.Contains(captureFailureKey));
    Assert.True(ReferenceEquals(captureCause, data[captureFailureKey]));
    // A fatal completion failure, not a frozen success: no ActionCompleted, no synthetic end
    // event, and the closed runtime refuses fresh queries and submissions.
    Assert.Equal(0, completedCount);
    Assert.Equal(0, battle.Events.EventsOf<SessionEndedBattleEvent>().Length);
    Assert.Throws<ObjectDisposedException>(() => battle.Query(new GetCompletedBattleQuery()));
    Assert.Throws<ObjectDisposedException>(() =>
      battle.Submit(BattleAction.EndFactionTurn(battle.PlayerFaction)));
  }

  [TestCase(TestName = "A stun reaction after the first movement step interrupts the remaining route")]
  public void StunReactionInterruptsRemainingMovementSteps()
  {
    var start = new Vector3I(0, 0, 0);
    var mid = new Vector3I(1, 0, 0);
    var end = new Vector3I(2, 0, 0);
    using var battle = BattleFixture.Solo(new Vector3I(4, 1, 4), start, health: 10, actionPoints: 5);
    var unit = battle.Unit;
    battle.RegisterHook<UnitMovedBattleEvent>(
      new DamageOnTileOccupiedHook<UnitMovedBattleEvent>(mid, unit, 10, oneShot: true, kind: DamageKind.Stun));

    BattleAction action = BattleAction.MoveUnit(battle.Alive(unit), [battle.At(mid), battle.At(end)]);
    BattleActionExecResult result = battle.Submit(action).RequireSome();

    Assert.Equal(1, result.EventsThatOccurred.ToArray().EventsOf<UnitMovedBattleEvent>().Length);
    Assert.Equal(mid, battle.PositionOf(unit).RequireSome().Raw);
    Assert.Equal(4, unit.CurrentActionPoints);
    Assert.True(unit.IsUnconscious);
  }

  [TestCase(TestName = "MoveUnit emits movement and tile occupation events after commit")]
  public void MoveUnitEmitsMovementAndTileOccupationEventsAfterCommit()
  {
    using var battle = BattleFixture.Solo(new Vector3I(4, 1, 4), new Vector3I(1, 0, 1), actionPoints: 5);
    var unit = battle.Unit;
    battle.ClearEvents();

    BattleAction move = BattleAction.MoveUnit(battle.Alive(unit), [battle.At(1, 0, 2)], 2);
    // The single guarded entry: direct execution outside the executor's submission step is
    // rejected before any cost, leaving the unconsumed action intact for a real submission.
    Assert.Throws<InvalidOperationException>(() => move.Execute(battle.Session));
    Assert.Equal(5, unit.CurrentActionPoints);
    Assert.Equal(new Vector3I(1, 0, 1), battle.PositionOf(unit).RequireSome().Raw);
    Assert.Equal(0, battle.Events.Count);

    battle.Submit(move);

    Assert.True(battle.Events.EventsOf<UnitMovedBattleEvent>().AsValueEnumerable().Any(battleEvent =>
      battleEvent.Position.Raw == new Vector3I(1, 0, 2) &&
      battleEvent.SourcePosition.Raw == new Vector3I(1, 0, 1)));
    Assert.True(battle.Events.EventsOf<TileOccupiedBattleEvent>().AsValueEnumerable().Any(battleEvent =>
      battleEvent.Position.Raw == new Vector3I(1, 0, 2)));
  }

  [TestCase(TestName = "BattleEventCommitted subscribers observe committed session state")]
  public void BattleEventCommittedSubscribersObserveCommittedSessionState()
  {
    using var battle = BattleFixture.Solo(new Vector3I(4, 1, 4), new Vector3I(1, 0, 1), actionPoints: 5);
    var unit = battle.Unit;
    var destination = new Vector3I(1, 0, 2);

    Option<Vector3I> observedPositionDuringEvent = None;
    battle.OnCommitted(battleEvent =>
    {
      if (battleEvent is UnitMovedBattleEvent)
        observedPositionDuringEvent = battle.PositionOf(unit).Map(point => point.Raw);
    });

    // A later subscriber's fault must not uncommit the step the earlier subscriber saw:
    // the cause surfaces after unwinding, and the hook pass never runs for the stream.
    var expected = new InvalidOperationException("subscriber sentinel");
    var observed = new List<BattleEvent>();
    var hook = new RecordingHook();
    battle.RegisterHook<UnitMovedBattleEvent>(hook);
    battle.OnCommitted(observed.Add);
    battle.OnCommitted(battleEvent =>
    {
      if (battleEvent is UnitMovedBattleEvent)
        throw expected;
    });

    var caught = Assert.Throws<InvalidOperationException>(() =>
      battle.Submit(BattleAction.MoveUnit(battle.Alive(unit), [battle.At(destination)], 2)));

    Assert.Equal(destination, observedPositionDuringEvent.RequireSome());
    Assert.True(ReferenceEquals(expected, caught));
    Assert.True(observed.AsValueEnumerable().Any(battleEvent => battleEvent is UnitMovedBattleEvent));
    // The mutation stayed committed: tile, cost, and options all reflect the step.
    Assert.Equal(destination, battle.PositionOf(unit).RequireSome().Raw);
    Assert.Equal(3, unit.CurrentActionPoints);
    Assert.Equal(0, hook.Received.Count);
    // A fresh submission finds no stale work: it advances the round exactly once.
    battle.Pass(unit);
    Assert.Equal(2, battle.Query(new GetCurrentTurnQuery()).RequireSome().RoundNumber);
  }

  [TestCase(TestName = "Executor evaluates matching hooks deterministically")]
  public void ExecutorEvaluatesMatchingHooksDeterministically()
  {
    using var battle = BattleFixture.Solo(new Vector3I(3, 1, 3), new Vector3I(0, 0, 0));
    var unit = battle.Unit;

    var log = new List<string>();
    var targetPosition = new Vector3I(1, 0, 0);
    battle.RegisterHook<UnitMovedBattleEvent>(new PositionRecordingHook(targetPosition, log, "late"), priority: 10);
    battle.RegisterHook<UnitMovedBattleEvent>(new PositionRecordingHook(targetPosition, log, "first"));
    battle.RegisterHook<UnitMovedBattleEvent>(new PositionRecordingHook(targetPosition, log, "second"));
    battle.RegisterHook<UnitDamagedBattleEvent>(new PositionRecordingHook(targetPosition, log, "ignored"), priority: -10);

    battle.Submit(BattleAction.MoveUnit(battle.Alive(unit), [battle.At(targetPosition)]));

    Assert.True(log.AsValueEnumerable().SequenceEqual(["first", "second", "late"]));
  }

  [TestCase(TestName = "Executor only evaluates registered event key bucket")]
  public void ExecutorOnlyEvaluatesRegisteredEventKeyBucket()
  {
    using var battle = BattleFixture.Solo(new Vector3I(3, 1, 3), new Vector3I(0, 0, 0));
    var unit = battle.Unit;
    var log = new List<string>();
    var targetPosition = new Vector3I(1, 0, 0);
    var ignoredHook = new PositionRecordingHook(targetPosition, log, "ignored");
    var matchingHook = new PositionRecordingHook(targetPosition, log, "matching");
    battle.RegisterHook<UnitDamagedBattleEvent>(ignoredHook);
    battle.RegisterHook<UnitMovedBattleEvent>(matchingHook);

    battle.Submit(BattleAction.MoveUnit(battle.Alive(unit), [battle.At(targetPosition)]));

    Assert.Equal(0, ignoredHook.EvaluateCallCount);
    Assert.Equal(1, matchingHook.EvaluateCallCount);
    Assert.True(log.AsValueEnumerable().SequenceEqual(["matching"]));
  }

  [TestCase(TestName = "Executor supports hook registered to event shape")]
  public void ExecutorSupportsHookRegisteredToEventShape()
  {
    using var battle = BattleFixture.Solo(new Vector3I(3, 1, 3), new Vector3I(0, 0, 0));
    var unit = battle.Unit;
    var log = new List<string>();
    var targetPosition = new Vector3I(1, 0, 0);
    battle.RegisterHook<IPositionedBattleEvent>(new PositionRecordingHook(targetPosition, log, "matched"));

    battle.Submit(BattleAction.MoveUnit(battle.Alive(unit), [battle.At(targetPosition)]));

    Assert.True(log.AsValueEnumerable().SequenceEqual(["matched", "matched"]));
  }

  [TestCase(TestName = "Executor consumes one-shot hooks before later actions")]
  public void ExecutorConsumesOneShotHooksBeforeLaterActions()
  {
    var start = new Vector3I(0, 0, 0);
    var targetPosition = new Vector3I(1, 0, 0);
    using var battle = BattleFixture.Solo(new Vector3I(3, 1, 3), start, actionPoints: 5);
    var unit = battle.Unit;
    var log = new List<string>();
    battle.RegisterHook<UnitMovedBattleEvent>(new OneShotRecordingHook(targetPosition, log, "matched"));

    BattleActionExecResult[] results =
    [
      battle.Submit(BattleAction.MoveUnit(battle.Alive(unit), [battle.At(targetPosition)])).RequireSome(),
      battle.Submit(BattleAction.MoveUnit(battle.Alive(unit), [battle.At(start)])).RequireSome(),
      battle.Submit(BattleAction.MoveUnit(battle.Alive(unit), [battle.At(targetPosition)])).RequireSome(),
    ];

    Assert.Equal(3, results.Length);
    Assert.True(log.AsValueEnumerable().SequenceEqual(["matched"]));
  }

  [TestCase(TestName = "Executor resolves interrupt actions in hook priority order")]
  public void ExecutorResolvesInterruptActionsInHookPriorityOrder()
  {
    var start = new Vector3I(0, 0, 0);
    var targetPosition = new Vector3I(1, 0, 0);
    using var battle = BattleFixture.Solo(new Vector3I(3, 1, 3), start, health: 10, actionPoints: 5);
    var unit = battle.Unit;
    battle.RegisterHook<UnitMovedBattleEvent>(
      new DamageOnTileOccupiedHook<UnitMovedBattleEvent>(targetPosition, unit, 2, oneShot: true), priority: 10);
    battle.RegisterHook<UnitMovedBattleEvent>(
      new DamageOnTileOccupiedHook<UnitMovedBattleEvent>(targetPosition, unit, 1, oneShot: true), priority: 0);

    battle.ClearEvents();
    battle.Submit(BattleAction.MoveUnit(battle.Alive(unit), [battle.At(targetPosition)]));

    int[] damageAmounts = battle.Events.EventsOf<UnitDamagedBattleEvent>()
      .AsValueEnumerable().Select(damaged => damaged.TotalAmount)
      .ToArray();

    Assert.True(damageAmounts.SequenceEqual([1, 2]));
  }

  [TestCase(TestName = "Executor resolves interrupts across a step's events in commit order")]
  public void ExecutorResolvesInterruptsAcrossEventsInCommitOrder()
  {
    var start = new Vector3I(0, 0, 0);
    var targetPosition = new Vector3I(1, 0, 0);
    using var battle = BattleFixture.Solo(new Vector3I(3, 1, 3), start, health: 10, actionPoints: 5);
    var unit = battle.Unit;
    // One move step commits UnitMoved then TileOccupied. A hook on each event pins the
    // aggregate-reverse-once contract: the FIRST event's interrupt resolves first. A per-event
    // reverse would flip this to [2, 1].
    battle.RegisterHook<UnitMovedBattleEvent>(
      new DamageOnTileOccupiedHook<UnitMovedBattleEvent>(targetPosition, unit, 1, oneShot: true));
    battle.RegisterHook<TileOccupiedBattleEvent>(
      new DamageOnTileOccupiedHook<TileOccupiedBattleEvent>(targetPosition, unit, 2, oneShot: true));

    battle.ClearEvents();
    battle.Submit(BattleAction.MoveUnit(battle.Alive(unit), [battle.At(targetPosition)]));

    int[] damageAmounts = battle.Events.EventsOf<UnitDamagedBattleEvent>()
      .AsValueEnumerable().Select(damaged => damaged.TotalAmount)
      .ToArray();

    Assert.True(damageAmounts.SequenceEqual([1, 2]));
  }

  [TestCase(TestName = "A rejected submission throws without consuming one-shot hooks")]
  public void RejectedSubmissionThrowsWithoutConsumingOneShotHooks()
  {
    using var battle = BattleFixture.Solo(new Vector3I(4, 1, 4), new Vector3I(0, 0, 0), health: 10);
    var unit = battle.Unit;
    var targetPosition = new Vector3I(2, 0, 0);
    battle.RegisterHook<TileOccupiedBattleEvent>(new DamageOnTileOccupiedHook<TileOccupiedBattleEvent>(targetPosition, unit, 3, oneShot: true));

    // (2, 0, 0) is not adjacent to (0, 0, 0): the malformed route rejects, which the
    // executor surfaces as an invariant-break throw.
    Assert.Throws<InvalidOperationException>(
      () => battle.Submit(BattleAction.MoveUnit(battle.Alive(unit), [battle.At(targetPosition)])));
    Assert.Equal(new Vector3I(0, 0, 0), battle.PositionOf(unit).RequireSome().Raw);
    Assert.Equal(10, unit.CurrentHealth);

    // The one-shot hook survived the rejected submission and still fires.
    battle.Submit(BattleAction.MoveUnit(battle.Alive(unit), [battle.At(1, 0, 0)]));
    battle.Submit(BattleAction.MoveUnit(battle.Alive(unit), [battle.At(targetPosition)]));
    Assert.Equal(7, unit.CurrentHealth);
  }

  [TestCase(TestName = "Executor supports multiple one-shot hook instances")]
  public void ExecutorSupportsMultipleOneShotHookInstances()
  {
    var start = new Vector3I(0, 0, 0);
    var firstTrap = new Vector3I(1, 0, 0);
    var secondTrap = new Vector3I(2, 0, 0);
    using var battle = BattleFixture.Solo(new Vector3I(4, 1, 4), start, health: 10, actionPoints: 5);
    var unit = battle.Unit;
    battle.RegisterHook<TileOccupiedBattleEvent>(
      new DamageOnTileOccupiedHook<TileOccupiedBattleEvent>(firstTrap, unit, 2, oneShot: true));
    battle.RegisterHook<TileOccupiedBattleEvent>(
      new DamageOnTileOccupiedHook<TileOccupiedBattleEvent>(secondTrap, unit, 3, oneShot: true));

    battle.Submit(BattleAction.MoveUnit(battle.Alive(unit), [battle.At(firstTrap), battle.At(secondTrap)]));

    Assert.Equal(5, unit.CurrentHealth);
    Assert.Equal(secondTrap, battle.PositionOf(unit).RequireSome().Raw);
  }

  [TestCase(TestName = "MoveUnit throws when an internal step becomes blocked")]
  public void MoveUnitThrowsWhenAnInternalStepBecomesBlocked()
  {
    var faction = TestData.MakeFaction("Player");
    var enemyFaction = TestData.MakeFaction("Enemy");
    using var battle = new BattleFixture(new Vector3I(4, 1, 4), [faction, enemyFaction]);
    var start = new Vector3I(0, 0, 0);
    var mid = new Vector3I(1, 0, 0);
    var end = new Vector3I(2, 0, 0);
    var unit = battle.Spawn(TestData.MakeCombatant("Alpha", faction, actionPoints: 5), start);
    battle.Spawn(TestData.MakeCombatant("Bystander", enemyFaction, actionPoints: 5), new Vector3I(3, 0, 0));
    battle.Start();

    var action = new MoveUnit(battle.Alive(unit), [battle.At(mid), battle.At(end)]);
    battle.RegisterHook<TileOccupiedBattleEvent>(
      new SpawnUnitOnTileOccupiedHook(mid, end, enemyFaction));

    // The blocker spawned by the mid-step interrupt occupies the route's next tile; the
    // blocked step rejects and the executor surfaces the invariant break. The first step
    // stays committed.
    Assert.Throws<InvalidOperationException>(() => battle.Submit(action));
    Assert.Equal(mid, battle.PositionOf(unit).RequireSome().Raw);
    Assert.True(battle.UnitAt(end) is not null);
  }

  [TestCase(TestName = "MoveUnit stops quietly when a reaction kills the mover mid route")]
  public void MoveUnitStopsQuietlyWhenAReactionKillsTheMoverMidRoute()
  {
    var start = new Vector3I(0, 0, 0);
    var mid = new Vector3I(1, 0, 0);
    var end = new Vector3I(2, 0, 0);
    using var battle = BattleFixture.Solo(new Vector3I(4, 1, 4), start, health: 3, actionPoints: 5);
    var unit = battle.Unit;

    var action = new MoveUnit(battle.Alive(unit), [battle.At(mid), battle.At(end)]);
    battle.RegisterHook<TileOccupiedBattleEvent>(new DamageOnTileOccupiedHook<TileOccupiedBattleEvent>(mid, unit, 3, oneShot: true));

    BattleActionExecResult result = battle.Submit(action).RequireSome();

    // Death interrupts the remainder of the route without failing the submission.
    Assert.True(ReferenceEquals(action, result.Action));
    Assert.Equal(0, unit.CurrentHealth);
    Assert.True(result.EventsThatOccurred.ToArray().EventsOf<UnitKilledBattleEvent>().Length > 0);
  }

  [TestCase(TestName = "MoveUnit fails malformed route before moving")]
  public void MoveUnitFailsMalformedRouteBeforeMoving()
  {
    var start = new Vector3I(0, 0, 0);
    var mid = new Vector3I(1, 0, 0);
    var nonAdjacent = new Vector3I(3, 0, 0);
    using var battle = BattleFixture.Solo(new Vector3I(4, 1, 4), start, actionPoints: 5);
    var unit = battle.Unit;

    var action = new MoveUnit(battle.Alive(unit), [battle.At(mid), battle.At(nonAdjacent)]);

    Assert.Throws<InvalidOperationException>(() => battle.Submit(action));
    Assert.Equal(start, battle.PositionOf(unit).RequireSome().Raw);
    Assert.Equal(5, unit.CurrentActionPoints);
    Assert.True(battle.Board.IsOccupied(battle.Board.At(0, 0, 0)));
  }

  [TestCase(TestName = "MoveUnit fails empty destinations before moving")]
  public void MoveUnitFailsEmptyDestinationsBeforeMoving()
  {
    var start = new Vector3I(0, 0, 0);
    using var battle = BattleFixture.Solo(new Vector3I(3, 1, 3), start, actionPoints: 5);
    var unit = battle.Unit;

    var action = new MoveUnit(battle.Alive(unit), []);

    Assert.Throws<InvalidOperationException>(() => battle.Submit(action));
    Assert.Equal(start, battle.PositionOf(unit).RequireSome().Raw);
  }

  [TestCase(TestName = "EndFactionTurn emits turn transition events after commit")]
  public void EndFactionTurnEmitsTurnTransitionEventsAfterCommit()
  {
    var factionA = TestData.MakeFaction("A");
    var factionB = TestData.MakeFaction("B");
    using var battle = new BattleFixture(new Vector3I(4, 1, 4), [factionA, factionB]);
    battle.Spawn(TestData.MakeCombatant("A1", factionA), new Vector3I(0, 0, 0));
    battle.Spawn(TestData.MakeCombatant("B1", factionB), new Vector3I(1, 0, 0));
    battle.Start();

    battle.ClearEvents();
    battle.Submit(BattleAction.EndFactionTurn(factionA));

    Assert.True(battle.Events.EventsOf<TurnEndedBattleEvent>().AsValueEnumerable().Any(battleEvent =>
      battleEvent.Faction == factionA &&
      battleEvent.TurnNumber == 1));
    Assert.True(battle.Events.EventsOf<TurnStartedBattleEvent>().AsValueEnumerable().Any(battleEvent =>
      battleEvent.Faction == factionB &&
      battleEvent.TurnNumber == 1));
  }

  [TestCase(TestName = "Executor resolves queued actions in FIFO order")]
  public void ExecutorResolvesQueuedActionsInFifoOrder()
  {
    var factionA = TestData.MakeFaction("A");
    var factionB = TestData.MakeFaction("B");
    using var battle = new BattleFixture(new Vector3I(4, 1, 4), [factionA, factionB]);
    var unitA = battle.Spawn(TestData.MakeCombatant("A1", factionA, actionPoints: 4), new Vector3I(0, 0, 0));
    battle.Spawn(TestData.MakeCombatant("B1", factionB), new Vector3I(1, 0, 0));
    battle.Start();

    BattleActionExecResult first = battle.Submit(BattleAction.MoveUnit(battle.Alive(unitA), [battle.At(0, 0, 1)])).RequireSome();
    BattleActionExecResult second = battle.Submit(BattleAction.EndFactionTurn(factionA)).RequireSome();

    Assert.True(first.Action is MoveUnit);
    Assert.True(second.Action is EndFactionTurn);
    Assert.Equal(factionB, battle.Query(new GetCurrentTurnQuery()).RequireSome().ActiveFaction);
  }

  [TestCase(TestName = "Executor submit returns one result per submitted action")]
  public void ExecutorSubmitReturnsOneResultPerSubmittedAction()
  {
    var factionA = TestData.MakeFaction("A");
    var factionB = TestData.MakeFaction("B");
    using var battle = new BattleFixture(new Vector3I(5, 1, 5), [factionA, factionB]);
    var unitA = battle.Spawn(TestData.MakeCombatant("A1", factionA, actionPoints: 5), new Vector3I(0, 0, 0));
    battle.Spawn(TestData.MakeCombatant("B1", factionB), new Vector3I(3, 0, 0));
    var grenade = TestData.MakeGrenade("Practice");
    unitA.AddInventoryItem(grenade.Item);
    battle.Start();

    BattleActionExecResult[] results =
    [
      battle.Submit(BattleAction.MoveUnit(battle.Alive(unitA), [battle.At(0, 0, 1)])).RequireSome(),
      battle.Submit(BattleAction.ThrowItem(battle.Alive(unitA), grenade, battle.At(2, 0, 1))).RequireSome(),
      battle.Submit(BattleAction.EndFactionTurn(factionA)).RequireSome(),
    ];

    Assert.Equal(3, results.Length);
    Assert.Equal(factionB, battle.Query(new GetCurrentTurnQuery()).RequireSome().ActiveFaction);
    Assert.False(unitA.HasInventoryItem(grenade.Item));
  }

  [TestCase(TestName = "Consumable throwable without charges is removed after one throw")]
  public void ConsumableThrowableWithoutChargesIsRemovedAfterOneThrow()
  {
    using var battle = BattleFixture.Solo(new Vector3I(5, 1, 5), new Vector3I(1, 0, 1), actionPoints: 4);
    var unit = battle.Unit;
    var throwable = TestData.MakeThrowable("Flare");
    unit.AddInventoryItem(throwable.Item);

    battle.Submit(BattleAction.ThrowItem(battle.Alive(unit), throwable, battle.At(3, 0, 1)));

    Assert.False(unit.HasInventoryItem(throwable.Item));
  }

  [TestCase(TestName = "Executor rejects stale end faction turn action after auto-advance")]
  public void ExecutorRejectsStaleEndFactionTurnActionAfterAutoAdvance()
  {
    var factionA = TestData.MakeFaction("A");
    var factionB = TestData.MakeFaction("B");
    using var battle = new BattleFixture(new Vector3I(4, 1, 4), [factionA, factionB]);
    var unitA = battle.Spawn(TestData.MakeCombatant("A1", factionA), new Vector3I(0, 0, 0));
    battle.Spawn(TestData.MakeCombatant("B1", factionB), new Vector3I(1, 0, 0));
    battle.Start();

    battle.Submit(BattleAction.PassUnit(battle.Alive(unitA)));
    Assert.Equal(factionB, battle.Query(new GetCurrentTurnQuery()).RequireSome().ActiveFaction);

    // Faction A's turn already auto-advanced; the stale submission is an invariant break.
    Assert.Throws<InvalidOperationException>(
      () => battle.Submit(BattleAction.EndFactionTurn(factionA)));
    Assert.Equal(factionB, battle.Query(new GetCurrentTurnQuery()).RequireSome().ActiveFaction);
  }

  private sealed partial class DamageOnTileOccupiedHook<TEventKey> : BattleHook
    where TEventKey : BattleEventTag
  {
    private readonly Vector3I _position;
    private readonly BattleUnitState _targetUnit;
    private readonly int _damage;
    private readonly bool _oneShot;
    private readonly DamageKind _kind;
    private bool _spent;

    public Option<Vector3I> ObservedTargetPositionDuringEvaluation { get; private set; }

    public DamageOnTileOccupiedHook(
      Vector3I position,
      BattleUnitState targetUnit,
      int damage,
      bool oneShot,
      DamageKind kind = DamageKind.Health)
    {
      _position = position;
      _targetUnit = targetUnit;
      _damage = damage;
      _oneShot = oneShot;
      _kind = kind;
    }

    public override bool NeedsToUnregister => _spent;

    public override IReadOnlyList<BattleAction> OnEvent(HookContext context, BattleEvent battleEvent)
    {
      if (battleEvent is not IPositionedBattleEvent positioned || positioned.Position.Raw != _position)
        return [];

      ObservedTargetPositionDuringEvaluation = context.Read.State.GetUnitPosition(_targetUnit)
        .Match(point => Some(point.Raw), () => None);

      if (_oneShot)
        _spent = true;

      System.Collections.Generic.List<BattleAction> interrupts = [];
      context.Read.State.TryGetAlive(_targetUnit)
        .IfSome(alive => interrupts.Add(BattleAction.ApplyDamage(alive, _damage, _kind)));
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
      return [BattleAction.SpawnUnit(TestData.MakeCombatant("Blocker", _faction), context.Read.State.Board.At(_spawnPosition))];
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

  // Loadout StatMod double: inert until armed; the armed TargetType read throws, faulting
  // every effective-stat resolution — the real Capture read path, without a production seam.
  private sealed partial class ArmedFaultStatMod(Exception failure) : StatMod
  {
    private bool _armed;

    public Exception Fault => failure;

    public void Arm() => _armed = true;

    public override Type TargetType
    {
      get
      {
        if (_armed)
          throw failure;
        return typeof(HealthStat);
      }
    }
  }

  // Terminal request, then arm the stat collaborator, then throw: earlier setup and the
  // broadcast succeeded, so the unwind's Capture read is what faults.
  private sealed class RequestEndArmThrowHook(ArmedFaultStatMod armedMod, Exception failure)
    : BattleHook<UnitMovedBattleEvent>
  {
    protected override IReadOnlyList<BattleAction> OnEvent(HookContext context, UnitMovedBattleEvent evt)
    {
      context.Read.RunningSession.IfSome(session => session.RequestEnd(BattleOutcome.Victory));
      armedMod.Arm();
      throw failure;
    }
  }

  // Real end-notification fault: records the callback-visible snapshot, mutates the original
  // campaign health stat (a recapture would read the changed value), then throws the sentinel.
  private sealed class CampaignMutationFaultOnSessionEndedHook(
    BattleFixture battle, BattleUnitState unit, Exception failure, Action<CompletedBattle, int> observe)
    : BattleHook<SessionEndedBattleEvent>
  {
    protected override IReadOnlyList<BattleAction> OnEvent(HookContext context, SessionEndedBattleEvent evt)
    {
      CompletedBattle callbackSnapshot = battle.Query(new GetCompletedBattleQuery()).RequireSome();
      unit.Combatant.GetStat<HealthStat>().BaseValue = 40;
      observe(callbackSnapshot, unit.MaxHealth);
      throw failure;
    }
  }

  // Throws the supplied failure on its first firing, whichever event key it is registered
  // under; the original exception identity is what the fault tests assert.
  private sealed class ThrowOnFirstEventHook(Exception failure) : BattleHook
  {
    private bool _thrown;

    public override IReadOnlyList<BattleAction> OnEvent(HookContext context, BattleEvent battleEvent)
    {
      if (_thrown)
        return [];
      _thrown = true;
      throw failure;
    }
  }

  // Requests a terminal outcome through the running receiver, the way an objective
  // directive does inside an accepted step.
  private sealed class RequestVictoryOnMovedHook : BattleHook<UnitMovedBattleEvent>
  {
    protected override IReadOnlyList<BattleAction> OnEvent(HookContext context, UnitMovedBattleEvent evt)
    {
      context.Read.RunningSession.IfSome(session => session.RequestEnd(BattleOutcome.Victory));
      return [];
    }
  }
}
