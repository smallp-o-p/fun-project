using FunProject.Battle;
using FunProject.Combatants;
using GdUnit4;
using Godot;
using System;
using System.Collections.Generic;

[TestSuite]
[RequireGodotRuntime]
public partial class BattleHookTest
{
  private sealed record ProbeBattleEvent : BattleEvent;

  private sealed class RaiseProbeOnTurnEndedHook : BattleHook
  {
    public override IReadOnlyList<BattleAction> OnEvent(HookContext context, BattleEvent battleEvent)
    {
      if (battleEvent is TurnEndedBattleEvent)
        context.Session.RaiseEvents(new ProbeBattleEvent());

      return [];
    }
  }

  private sealed partial class CountingHook : BattleHook
  {
    public int Evaluations { get; private set; }

    public override IReadOnlyList<BattleAction> OnEvent(HookContext context, BattleEvent battleEvent)
    {
      if (battleEvent is not ProbeBattleEvent)
        return [];

      Evaluations++;
      return [];
    }
  }

  private static (BattleSession session, BattleActionExecutor executor, Faction faction) MakeSessionWithUnit()
  {
    var faction = BattleTestFactory.MakeFaction("Player");
    var session = BattleTestFactory.MakeSession(new Vector3I(4, 1, 4), [faction]);
    SpawnUnit(session, BattleTestFactory.MakeCombatant("Alpha", faction), new Vector3I(1, 0, 1));
    var executor = ExecutorFor(session);
    return (session, executor, faction);
  }

  [TestCase(TestName = "Hook registered for a concrete event type receives only that event")]
  public void HookReceivesOnlyItsConcreteEvent()
  {
    var (session, executor, _) = MakeSessionWithUnit();
    var turnStartedListener = new RecordingHook();
    var damagedListener = new RecordingHook();
    executor.RegisterHook<TurnStartedBattleEvent>(turnStartedListener);
    executor.RegisterHook<UnitDamagedBattleEvent>(damagedListener);

    StartBattle(session);

    Assert.Equal(1, turnStartedListener.Received.Count);
    Assert.True(turnStartedListener.Received.AsValueEnumerable().Single() is TurnStartedBattleEvent);
    Assert.Equal(0, damagedListener.Received.Count);
  }

  [TestCase(TestName = "Hook registered for a marker interface receives implementing events")]
  public void HookReceivesMarkerInterfaceEvents()
  {
    var faction = BattleTestFactory.MakeFaction("Player");
    var session = BattleTestFactory.MakeSession(new Vector3I(4, 1, 4), [faction]);
    var unitEventsListener = new RecordingHook();
    ExecutorFor(session).RegisterHook<IUnitBattleEvent>(unitEventsListener);

    SpawnUnit(session, BattleTestFactory.MakeCombatant("Alpha", faction), new Vector3I(1, 0, 1));

    Assert.True(unitEventsListener.Received.AsValueEnumerable().OfType<UnitAddedBattleEvent>().Any());
  }

  [TestCase(TestName = "A hook registered under concrete and interface keys fires once for one event")]
  public void OneHookRegisteredUnderMultipleMatchingKeysFiresOnce()
  {
    var (session, executor, faction) = MakeSessionWithUnit();
    var unit = session.GetFactionAliveUnits(faction).AsValueEnumerable().First();
    var hook = new RecordingHook();
    executor.RegisterHook<UnitKilledBattleEvent>(hook);
    executor.RegisterHook<IUnitBattleEvent>(hook);

    ApplyDamage(session, unit, 999);

    Assert.Equal(1, hook.Received.Count);
  }

  [TestCase(TestName = "Event raised by a hook dispatches after the current event and reaches other hooks")]
  public void HookRaisedEventDispatchesAfterCurrentEvent()
  {
    var (session, executor, faction) = MakeSessionWithUnit();
    executor.RegisterHook<TurnEndedBattleEvent>(new RaiseProbeOnTurnEndedHook());
    var hook = new CountingHook();
    executor.RegisterHook<ProbeBattleEvent>(hook);
    StartBattle(session);

    var recorder = new BattleEventRecorder(session);
    executor.Submit(BattleAction.EndFactionTurn(faction));

    recorder.AssertCommittedBefore<TurnEndedBattleEvent, ProbeBattleEvent>();
    recorder.AssertCommittedBefore<ProbeBattleEvent, TurnStartedBattleEvent>();
    Assert.Equal(1, hook.Evaluations);
  }

  private sealed class RaiseThenThrowOnceOnTurnEndedHook : BattleHook
  {
    private bool _hasThrown;

    public override IReadOnlyList<BattleAction> OnEvent(HookContext context, BattleEvent battleEvent)
    {
      if (_hasThrown || battleEvent is not TurnEndedBattleEvent)
        return [];

      _hasThrown = true;
      context.Session.RaiseEvents(new ProbeBattleEvent());
      throw new InvalidOperationException("Hook failure.");
    }
  }

  [TestCase(TestName = "A throwing hook propagates out of Submit and clears undispatched events")]
  public void ThrowingHookPropagatesAndClearsQueue()
  {
    var (session, executor, faction) = MakeSessionWithUnit();
    executor.RegisterHook<TurnEndedBattleEvent>(new RaiseThenThrowOnceOnTurnEndedHook());
    StartBattle(session);

    var recorder = new BattleEventRecorder(session);
    // The simple execution model lets a hook failure surface as the throw it is; the
    // session clears its dispatch queue on the way out.
    Assert.Throws<InvalidOperationException>(
      () => executor.Submit(BattleAction.EndFactionTurn(faction)));
    Assert.False(recorder.OfType<ProbeBattleEvent>().AsValueEnumerable().Any());

    executor.Submit(BattleAction.EndFactionTurn(faction));
    Assert.False(recorder.OfType<ProbeBattleEvent>().AsValueEnumerable().Any());
  }

  private sealed partial class MarkingHook : BattleHook
  {
    private readonly Action _mark;

    public MarkingHook(Action mark) => _mark = mark;

    public override IReadOnlyList<BattleAction> OnEvent(HookContext context, BattleEvent battleEvent)
    {
      _mark();
      return [];
    }
  }

  private sealed partial class InterruptingHook : BattleHook
  {
    private readonly Func<HookContext, BattleAction> _makeInterrupt;

    public InterruptingHook(Func<HookContext, BattleAction> makeInterrupt) => _makeInterrupt = makeInterrupt;

    public Option<BattleAction> ObservedSourceAction { get; private set; }

    public override IReadOnlyList<BattleAction> OnEvent(HookContext context, BattleEvent battleEvent)
    {
      ObservedSourceAction = context.SourceAction;
      return [_makeInterrupt(context)];
    }
  }

  [TestCase(TestName = "A hook returning interrupts outside an executor action throws")]
  public void InterruptsOutsideExecutorActionThrow()
  {
    var (session, executor, _) = MakeSessionWithUnit();
    executor.RegisterHook<TurnStartedBattleEvent>(
      new InterruptingHook(context => BattleAction.EndFactionTurn(context.Session.ActiveSide)));

    // StartBattle raises TurnStarted with no executor action in flight. Replicate the
    // StartBattle(session) test helper's objective-assignment BEFORE this point, or the
    // no-objective guard throws the same exception type and the test false-passes; then
    // assert on the message to pin the right throw.
    EnsureEveryFactionHasObjective(session);

    InvalidOperationException thrown = null;
    try
    {
      session.StartBattle();
    }
    catch (InvalidOperationException exception)
    {
      thrown = exception;
    }

    Assert.True(thrown is not null, "Expected StartBattle to throw when a hook returns interrupts outside an executor action.");
    Assert.True(thrown.Message.Contains("interrupt"));
  }

  [TestCase(TestName = "Hooks receive the in-flight action as SourceAction during executor dispatches")]
  public void HooksReceiveSourceActionInsideExecutorDispatch()
  {
    var (session, executor, faction) = MakeSessionWithUnit();
    StartBattle(session);
    var hook = new InterruptingHook(context => BattleAction.PassUnit(
      context.Session.TryGetAlive(context.Session.GetFactionAliveUnits(faction).AsValueEnumerable().First()).RequireSome()));
    executor.RegisterHook<UnitMovedBattleEvent>(hook);

    var unit = session.GetFactionAliveUnits(faction).AsValueEnumerable().First();
    executor.Submit(BattleAction.MoveUnit(session.TryGetAlive(unit).RequireSome(), [session.Board.At(1, 0, 2)]))
      ;

    Assert.True(hook.ObservedSourceAction.IsSome);
    Assert.True(hook.ObservedSourceAction.RequireSome() is MoveUnit);
  }

  [TestCase(TestName = "One-shot hook retires itself by signalling NeedsToUnregister")]
  public void OneShotHookRetiresItselfThroughNeedsToUnregister()
  {
    var (session, executor, _, _) = StartSoloBattle(new Vector3I(3, 1, 3), new Vector3I(0, 0, 0));

    var hook = new SelfRetiringHook();
    executor.RegisterHook<TurnStartedBattleEvent>(hook);

    AdvanceTurn(session);
    AdvanceTurn(session);

    Assert.Equal(1, hook.Evaluations);
  }

  [TestCase(TestName = "A retired one-shot hook is removed from every key it was registered under")]
  public void RetiredOneShotHookIsRemovedFromEveryKey()
  {
    var (session, executor, faction, _) = StartSoloBattle(new Vector3I(3, 1, 3), new Vector3I(0, 0, 0));

    // Same instance under two keys: retiring after the UnitMoved firing must also clear
    // the TurnStarted registration — the registry owns removal, not the hook.
    var hook = new SelfRetiringHook();
    executor.RegisterHook<UnitMovedBattleEvent>(hook);
    executor.RegisterHook<TurnStartedBattleEvent>(hook);

    var unit = session.GetFactionAliveUnits(faction).AsValueEnumerable().First();
    executor.Submit(BattleAction.MoveUnit(session.TryGetAlive(unit).RequireSome(), [session.Board.At(1, 0, 0)]));

    AdvanceTurn(session);
    AdvanceTurn(session);

    Assert.Equal(1, hook.Evaluations);
  }

  private sealed class SelfRetiringHook : BattleHook
  {
    private bool _spent;

    public int Evaluations { get; private set; }

    public override bool NeedsToUnregister => _spent;

    public override IReadOnlyList<BattleAction> OnEvent(HookContext context, BattleEvent battleEvent)
    {
      Evaluations++;
      _spent = true;
      return [];
    }
  }
}
