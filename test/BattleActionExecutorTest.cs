using FunProject.Battle;
using FunProject.Combatants;
using FunProject.Weapons;
using GdUnit4;
using Godot;
using System;
using System.Collections.Generic;

namespace FunProject.Tests;

[TestSuite]
[RequireGodotRuntime]
public partial class BattleActionExecutorTest
{
  // Factory-ready fixture: one unit per side at the given positions.
  private static BattleFixture DuelFixture(Faction factionA, string nameA, Vector3I posA,
    Faction factionB, string nameB, Vector3I posB, Vector3I? dimensions = null,
    int aActionPoints = 4)
    => new(new BattleSetup(
      TestData.MakeOpenBattleMap(dimensions ?? new Vector3I(4, 1, 4)),
      [
        new BattleSideSetup(factionA, [new FakeObjectiveData()],
          [new UnitPlacement(new UnitLoadout(TestData.MakeCombatant(nameA, factionA, actionPoints: aActionPoints)), posA)]),
        new BattleSideSetup(factionB, [new FakeObjectiveData()],
          [new UnitPlacement(new UnitLoadout(TestData.MakeCombatant(nameB, factionB)), posB)]),
      ],
      Seed: 7));

  [TestCase(false, true, TestName = "Submit resolves a hook's interrupt within the same submission")]
  [TestCase(true, true, TestName = "Submit reports the submitted action once and resolves interrupts between its steps")]
  [TestCase(true, false, TestName = "MoveUnit submit reports only the submitted action")]
  public static void SubmitReportsMovementAndInterrupts(bool twoSteps, bool reaction)
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
    BattleActionExecResult result = battle.Submit(submittedAction);

    Assert.True(ReferenceEquals(submittedAction, result.Action));
    Assert.Equal(twoSteps ? end : mid, battle.Session.GetUnitPosition(unit).RequireSome().Raw);
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
  public static void StaleInterruptOnDeadTargetIsInterruptedNotThrown()
  {
    var factionA = TestData.MakeFaction("Alpha");
    var factionB = TestData.MakeFaction("Bravo");
    using var battle = new BattleFixture(new BattleSetup(
      TestData.MakeOpenBattleMap(new Vector3I(3, 1, 3)),
      [
        new BattleSideSetup(factionA, [new FakeObjectiveData()],
          [new UnitPlacement(new UnitLoadout(TestData.MakeCombatant("Mover", factionA, health: 20, actionPoints: 5)), new Vector3I(0, 0, 0))]),
        new BattleSideSetup(factionB, [new FakeObjectiveData()],
          [new UnitPlacement(new UnitLoadout(TestData.MakeCombatant("Victim", factionB, health: 10)), new Vector3I(2, 0, 0))]),
      ],
      Seed: 7));
    var mover = battle.UnitAt(new Vector3I(0, 0, 0));
    var victim = battle.UnitAt(new Vector3I(2, 0, 0));
    battle.RegisterHook<UnitMovedBattleEvent>(new InterruptActionsHook(
      context =>
      [
        BattleAction.ApplyDamage(context.Session.TryGetAlive(victim).RequireSome(), 10),
        BattleAction.ApplyDamage(context.Session.TryGetAlive(victim).RequireSome(), 5),
      ]));

    // The first interrupt kills the victim; the second was constructed against a proof
    // that is now stale and must be interrupted quietly rather than throw out of Submit.
    battle.Submit(BattleAction.MoveUnit(battle.Alive(mover), [battle.At(0, 0, 1)]));

    Assert.True(victim.IsDead);
  }

  [TestCase(TestName = "Interrupts whose actor died earlier in the submission are interrupted")]
  public static void InterruptWithDeadActorIsInterrupted()
  {
    using var battle = BattleFixture.Solo(new Vector3I(3, 1, 3), new Vector3I(0, 0, 0), health: 10, actionPoints: 5);
    var unit = battle.Unit;
    var usable = TestData.MakeUsableItem("Medkit", maxCharges: 2);
    unit.AddInventoryItem(usable.Item);
    battle.ClearEvents();
    battle.RegisterHook<UnitMovedBattleEvent>(new InterruptActionsHook(
      context =>
      [
        BattleAction.ApplyDamage(context.Session.TryGetAlive(unit).RequireSome(), 10),
        BattleAction.UseItem(context.Session.TryGetAlive(unit).RequireSome(), usable),
      ]));

    // The first interrupt kills the mover; its own queued UseItem must interrupt without
    // spending a charge, AP, or raising ItemUsed.
    battle.Submit(BattleAction.MoveUnit(battle.Alive(unit), [battle.At(1, 0, 0)]));

    Assert.True(unit.IsDead);
    Assert.Equal(2, usable.Capability.Current);
    Assert.False(battle.Events.EventsOf<ItemUsedBattleEvent>().AsValueEnumerable().Any());
  }

  [TestCase(TestName = "A failed submission is unwound; the next Submit does not resume stale work")]
  public static void FailedSubmissionIsUnwoundForNextSubmit()
  {
    using var battle = BattleFixture.Solo(new Vector3I(3, 1, 3), new Vector3I(0, 0, 0));
    var faction = battle.PlayerFaction;
    battle.RegisterHook<TurnEndedBattleEvent>(new ThrowOnFirstTurnEndedHook());

    Assert.Throws<InvalidOperationException>(
      () => battle.Submit(BattleAction.EndFactionTurn(faction)));

    // The failed submission must leave nothing queued: the recovery submission advances the
    // round exactly once (a stale queued EndFactionTurn would advance it twice).
    battle.Submit(BattleAction.EndFactionTurn(faction));

    Assert.Equal(2, battle.Session.TurnNumber);
  }

  [TestCase(TestName = "A stun reaction after the first movement step interrupts the remaining route")]
  public static void StunReactionInterruptsRemainingMovementSteps()
  {
    var start = new Vector3I(0, 0, 0);
    var mid = new Vector3I(1, 0, 0);
    var end = new Vector3I(2, 0, 0);
    using var battle = BattleFixture.Solo(new Vector3I(4, 1, 4), start, health: 10, actionPoints: 5);
    var unit = battle.Unit;
    battle.RegisterHook<UnitMovedBattleEvent>(
      new DamageOnTileOccupiedHook<UnitMovedBattleEvent>(mid, unit, 10, oneShot: true, kind: DamageKind.Stun));

    BattleAction action = BattleAction.MoveUnit(battle.Alive(unit), [battle.At(mid), battle.At(end)]);
    BattleActionExecResult result = battle.Submit(action);

    Assert.Equal(1, result.EventsThatOccurred.ToArray().EventsOf<UnitMovedBattleEvent>().Length);
    Assert.Equal(mid, battle.Session.GetUnitPosition(unit).RequireSome().Raw);
    Assert.Equal(4, unit.CurrentActionPoints);
    Assert.True(unit.IsUnconscious);
  }

  [TestCase(false)]
  [TestCase(true)]
  public static void PlayerPassEndsActivationDespiteLostCapacity(bool unconscious)
  {
    using var battle = BattleFixture.Duel();
    var pass = BattleAction.PassUnit(battle.Alive(battle.PlayerUnit));
    int actionPoints = battle.PlayerUnit.CurrentActionPoints;
    if (unconscious) battle.PlayerUnit.ReceiveStun(battle.PlayerUnit.CurrentHealth);
    else battle.PlayerUnit.ApplyStatusEffect(TestData.MakeStun(duration: 2));
    var result = battle.Submit(pass);
    Assert.Equal(1, result.EventsThatOccurred.ToArray().EventsOf<UnitActivationEndedBattleEvent>().Length);
    Assert.Equal(actionPoints, battle.PlayerUnit.CurrentActionPoints);
    Assert.False(battle.Query(new CanUnitActNow(battle.PlayerUnit)));
    Assert.Equal(battle.EnemyFaction, battle.Query(new GetActiveSideQuery()));
  }

  [TestCase]
  public static void PassWithDeadActorRejectsStaleParametersInsteadOfInterrupting()
  {
    using var battle = BattleFixture.Duel();
    var pass = BattleAction.PassUnit(battle.Alive(battle.PlayerUnit));
    battle.ApplyDamage(battle.PlayerUnit, 999);
    battle.ClearEvents();
    Assert.Throws<InvalidOperationException>(() => battle.Submit(pass));
    Assert.Equal(0, battle.Events.Count);
  }

  [TestCase(TestName = "MoveUnit emits movement and tile occupation events after commit")]
  public static void MoveUnitEmitsMovementAndTileOccupationEventsAfterCommit()
  {
    using var battle = BattleFixture.Solo(new Vector3I(4, 1, 4), new Vector3I(1, 0, 1), actionPoints: 5);
    var unit = battle.Unit;
    battle.ClearEvents();

    battle.Submit(BattleAction.MoveUnit(battle.Alive(unit), [battle.At(1, 0, 2)], 2));

    Assert.True(battle.Events.EventsOf<UnitMovedBattleEvent>().AsValueEnumerable().Any(battleEvent =>
      battleEvent.Position.Raw == new Vector3I(1, 0, 2) &&
      battleEvent.SourcePosition.Raw == new Vector3I(1, 0, 1)));
    Assert.True(battle.Events.EventsOf<TileOccupiedBattleEvent>().AsValueEnumerable().Any(battleEvent =>
      battleEvent.Position.Raw == new Vector3I(1, 0, 2)));
  }

  [TestCase(TestName = "BattleEventCommitted subscribers observe committed session state")]
  public static void BattleEventCommittedSubscribersObserveCommittedSessionState()
  {
    using var battle = BattleFixture.Solo(new Vector3I(4, 1, 4), new Vector3I(1, 0, 1), actionPoints: 5);
    var session = battle.Session;
    var unit = battle.Unit;
    var destination = new Vector3I(1, 0, 2);

    Option<Vector3I> observedPositionDuringEvent = None;
    session.BattleEventCommitted += battleEvent =>
    {
      if (battleEvent is UnitMovedBattleEvent)
        observedPositionDuringEvent = session.GetUnitPosition(unit).Map(point => point.Raw);
    };

    battle.Submit(BattleAction.MoveUnit(battle.Alive(unit), [battle.At(destination)], 2));

    Assert.Equal(destination, observedPositionDuringEvent.RequireSome());
  }

  [TestCase(TestName = "Executor supports hook registered to event shape")]
  public static void ExecutorSupportsHookRegisteredToEventShape()
  {
    using var battle = BattleFixture.Solo(new Vector3I(3, 1, 3), new Vector3I(0, 0, 0));
    var unit = battle.Unit;
    var log = new List<string>();
    var targetPosition = new Vector3I(1, 0, 0);
    battle.RegisterHook<IPositionedBattleEvent>(new PositionRecordingHook(targetPosition, log, "matched"));

    battle.Submit(BattleAction.MoveUnit(battle.Alive(unit), [battle.At(targetPosition)]));

    Assert.True(log.AsValueEnumerable().SequenceEqual(["matched", "matched"]));
  }

  [TestCase(TestName = "Executor resolves interrupt actions in hook priority order")]
  public static void ExecutorResolvesInterruptActionsInHookPriorityOrder()
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
  public static void ExecutorResolvesInterruptsAcrossEventsInCommitOrder()
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
  public static void RejectedSubmissionThrowsWithoutConsumingOneShotHooks()
  {
    using var battle = BattleFixture.Solo(new Vector3I(4, 1, 4), new Vector3I(0, 0, 0), health: 10);
    var unit = battle.Unit;
    var targetPosition = new Vector3I(2, 0, 0);
    battle.RegisterHook<TileOccupiedBattleEvent>(new DamageOnTileOccupiedHook<TileOccupiedBattleEvent>(targetPosition, unit, 3, oneShot: true));

    // (2, 0, 0) is not adjacent to (0, 0, 0): the malformed route rejects, which the
    // executor surfaces as an invariant-break throw.
    Assert.Throws<InvalidOperationException>(
      () => battle.Submit(BattleAction.MoveUnit(battle.Alive(unit), [battle.At(targetPosition)])));
    Assert.Equal(new Vector3I(0, 0, 0), battle.Session.GetUnitPosition(unit).RequireSome().Raw);
    Assert.Equal(10, unit.CurrentHealth);

    // The one-shot hook survived the rejected submission and still fires.
    battle.Submit(BattleAction.MoveUnit(battle.Alive(unit), [battle.At(1, 0, 0)]));
    battle.Submit(BattleAction.MoveUnit(battle.Alive(unit), [battle.At(targetPosition)]));
    Assert.Equal(7, unit.CurrentHealth);
  }

  [TestCase(TestName = "Executor supports multiple one-shot hook instances")]
  public static void ExecutorSupportsMultipleOneShotHookInstances()
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
    Assert.Equal(secondTrap, battle.Session.GetUnitPosition(unit).RequireSome().Raw);
  }

  [TestCase(TestName = "MoveUnit throws when an internal step becomes blocked")]
  public static void MoveUnitThrowsWhenAnInternalStepBecomesBlocked()
  {
    var faction = TestData.MakeFaction("Player");
    var enemyFaction = TestData.MakeFaction("Enemy");
    using var battle = DuelFixture(faction, "Alpha", new Vector3I(0, 0, 0), enemyFaction, "Seed", new Vector3I(3, 0, 3), aActionPoints: 5);
    var start = new Vector3I(0, 0, 0);
    var mid = new Vector3I(1, 0, 0);
    var end = new Vector3I(2, 0, 0);
    var unit = battle.Unit;

    var action = new MoveUnit(battle.Alive(unit), [battle.At(mid), battle.At(end)]);
    battle.RegisterHook<TileOccupiedBattleEvent>(
      new SpawnUnitOnTileOccupiedHook(mid, end, enemyFaction));

    // The blocker spawned by the mid-step interrupt occupies the route's next tile; the
    // blocked step rejects and the executor surfaces the invariant break. The first step
    // stays committed.
    Assert.Throws<InvalidOperationException>(() => battle.Submit(action));
    Assert.Equal(mid, battle.Session.GetUnitPosition(unit).RequireSome().Raw);
    Assert.True(battle.Session.GetUnitAt(battle.Board.At(end)).IsSome);
  }

  [TestCase(TestName = "MoveUnit stops quietly when a reaction kills the mover mid route")]
  public static void MoveUnitStopsQuietlyWhenAReactionKillsTheMoverMidRoute()
  {
    var start = new Vector3I(0, 0, 0);
    var mid = new Vector3I(1, 0, 0);
    var end = new Vector3I(2, 0, 0);
    using var battle = BattleFixture.Solo(new Vector3I(4, 1, 4), start, health: 3, actionPoints: 5);
    var unit = battle.Unit;

    var action = new MoveUnit(battle.Alive(unit), [battle.At(mid), battle.At(end)]);
    battle.RegisterHook<TileOccupiedBattleEvent>(new DamageOnTileOccupiedHook<TileOccupiedBattleEvent>(mid, unit, 3, oneShot: true));

    BattleActionExecResult result = battle.Submit(action);

    // Death interrupts the remainder of the route without failing the submission.
    Assert.True(ReferenceEquals(action, result.Action));
    Assert.Equal(0, unit.CurrentHealth);
    Assert.True(result.EventsThatOccurred.ToArray().EventsOf<UnitKilledBattleEvent>().Length > 0);
  }

  [TestCase(TestName = "MoveUnit fails malformed route before moving")]
  public static void MoveUnitFailsMalformedRouteBeforeMoving()
  {
    var start = new Vector3I(0, 0, 0);
    var mid = new Vector3I(1, 0, 0);
    var nonAdjacent = new Vector3I(3, 0, 0);
    using var battle = BattleFixture.Solo(new Vector3I(4, 1, 4), start, actionPoints: 5);
    var unit = battle.Unit;

    var action = new MoveUnit(battle.Alive(unit), [battle.At(mid), battle.At(nonAdjacent)]);

    Assert.Throws<InvalidOperationException>(() => battle.Submit(action));
    Assert.Equal(start, battle.Session.GetUnitPosition(unit).RequireSome().Raw);
    Assert.Equal(5, unit.CurrentActionPoints);
    Assert.True(battle.Session.Board.IsOccupied(battle.Session.Board.At(0, 0, 0)));
  }

  [TestCase(TestName = "MoveUnit fails empty destinations before moving")]
  public static void MoveUnitFailsEmptyDestinationsBeforeMoving()
  {
    var start = new Vector3I(0, 0, 0);
    using var battle = BattleFixture.Solo(new Vector3I(3, 1, 3), start, actionPoints: 5);
    var unit = battle.Unit;

    var action = new MoveUnit(battle.Alive(unit), []);

    Assert.Throws<InvalidOperationException>(() => battle.Submit(action));
    Assert.Equal(start, battle.Session.GetUnitPosition(unit).RequireSome().Raw);
  }

  [TestCase(TestName = "EndFactionTurn emits turn transition events after commit")]
  public static void EndFactionTurnEmitsTurnTransitionEventsAfterCommit()
  {
    var factionA = TestData.MakeFaction("A");
    var factionB = TestData.MakeFaction("B");
    using var battle = DuelFixture(factionA, "A1", new Vector3I(0, 0, 0), factionB, "B1", new Vector3I(1, 0, 0));
    var session = battle.Session;
    var unitA = battle.UnitAt(new Vector3I(0, 0, 0));
    var unitB = battle.UnitAt(new Vector3I(1, 0, 0));
    var sideFlips = new List<(Faction FlippedTo, Faction ActiveSide, int TurnNumber, bool OutgoingAvailable, bool IncomingAvailable)>();
    session.BattleEventCommitted += battleEvent =>
    {
      if (battleEvent is not ActiveSideChangedBattleEvent changed)
        return;
      BattleUnitState outgoing = changed.Faction == factionA ? unitB : unitA;
      BattleUnitState incoming = changed.Faction == factionA ? unitA : unitB;
      sideFlips.Add((changed.Faction, session.ActiveSide, session.TurnNumber,
        session.IsUnitStillAvailableThisTurn(outgoing),
        session.IsUnitStillAvailableThisTurn(incoming)));
    };

    battle.ClearEvents();
    battle.Submit(BattleAction.EndFactionTurn(factionA));
    battle.Submit(BattleAction.EndFactionTurn(factionB));

    Assert.True(battle.Events.EventsOf<TurnEndedBattleEvent>().AsValueEnumerable().Any(battleEvent =>
      battleEvent.Faction == factionA &&
      battleEvent.TurnNumber == 1));
    Assert.True(battle.Events.EventsOf<TurnStartedBattleEvent>().AsValueEnumerable().Any(battleEvent =>
      battleEvent.Faction == factionB &&
      battleEvent.TurnNumber == 1));
    Assert.True(battle.Events.EventsOf<TurnEndedBattleEvent>().AsValueEnumerable().Any(battleEvent =>
      battleEvent.Faction == factionB &&
      battleEvent.TurnNumber == 1));
    Assert.True(battle.Events.EventsOf<TurnStartedBattleEvent>().AsValueEnumerable().Any(battleEvent =>
      battleEvent.Faction == factionA &&
      battleEvent.TurnNumber == 2));

    // The scheduling transition is complete before any event observer runs: the active
    // identity is the flipped-to side, the outgoing side's availability is already cleared,
    // the incoming side's is established, and the round increment precedes the notifications.
    Assert.Equal(2, sideFlips.Count);
    Assert.Equal((factionB, factionB, 1, false, true), sideFlips[0]);
    Assert.Equal((factionA, factionA, 2, false, true), sideFlips[1]);
  }

  [TestCase(TestName = "Consumable throwable without charges is removed after one throw")]
  public static void ConsumableThrowableWithoutChargesIsRemovedAfterOneThrow()
  {
    using var battle = BattleFixture.Solo(new Vector3I(5, 1, 5), new Vector3I(1, 0, 1), actionPoints: 4);
    var unit = battle.Unit;
    var throwable = TestData.MakeThrowable("Flare");
    unit.AddInventoryItem(throwable.Item);

    battle.Submit(BattleAction.ThrowItem(battle.Alive(unit), throwable, battle.At(3, 0, 1)));

    Assert.False(unit.HasInventoryItem(throwable.Item));
  }

  [TestCase(TestName = "Executor rejects stale end faction turn action after auto-advance")]
  public static void ExecutorRejectsStaleEndFactionTurnActionAfterAutoAdvance()
  {
    var factionA = TestData.MakeFaction("A");
    var factionB = TestData.MakeFaction("B");
    using var battle = DuelFixture(factionA, "A1", new Vector3I(0, 0, 0), factionB, "B1", new Vector3I(1, 0, 0));
    var unitA = battle.Unit;

    battle.Submit(BattleAction.PassUnit(battle.Alive(unitA)));
    Assert.Equal(factionB, battle.Session.ActiveSide);

    // Faction A's turn already auto-advanced; the stale submission is an invariant break.
    Assert.Throws<InvalidOperationException>(
      () => battle.Submit(BattleAction.EndFactionTurn(factionA)));
    Assert.Equal(factionB, battle.Session.ActiveSide);
  }

  private sealed partial class DamageOnTileOccupiedHook<TEventKey> : BattleHook
    where TEventKey : IBattleEventTag
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

      ObservedTargetPositionDuringEvaluation = context.Session.GetUnitPosition(_targetUnit)
        .Match(point => Some(point.Raw), () => None);

      if (_oneShot)
        _spent = true;

      System.Collections.Generic.List<BattleAction> interrupts = [];
      context.Session.TryGetAlive(_targetUnit)
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
      return [BattleAction.SpawnUnit(TestData.MakeCombatant("Blocker", _faction), context.Session.Board.At(_spawnPosition))];
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
