using FunProject.Battle;
using FunProject.Combatants;
using GdUnit4;
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

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
    var executor = new BattleActionExecutor(session);
    return (session, executor, faction);
  }

  [TestCase(TestName = "Hook registered for a concrete event type receives only that event")]
  public void HookReceivesOnlyItsConcreteEvent()
  {
    var (session, _, _) = MakeSessionWithUnit();
    var turnStartedListener = new RecordingHook();
    var damagedListener = new RecordingHook();
    session.RegisterHook<TurnStartedBattleEvent>(turnStartedListener, HookPhase.After);
    session.RegisterHook<UnitDamagedBattleEvent>(damagedListener, HookPhase.After);

    StartBattle(session);

    Assert.Equal(1, turnStartedListener.Received.Count);
    Assert.True(turnStartedListener.Received.Single() is TurnStartedBattleEvent);
    Assert.Equal(0, damagedListener.Received.Count);
  }

  [TestCase(TestName = "Hook registered for a marker interface receives implementing events")]
  public void HookReceivesMarkerInterfaceEvents()
  {
    var faction = BattleTestFactory.MakeFaction("Player");
    var session = BattleTestFactory.MakeSession(new Vector3I(4, 1, 4), [faction]);
    var unitEventsListener = new RecordingHook();
    session.RegisterHook<IUnitBattleEvent>(unitEventsListener, HookPhase.After);

    SpawnUnit(session, BattleTestFactory.MakeCombatant("Alpha", faction), new Vector3I(1, 0, 1));

    Assert.True(unitEventsListener.Received.OfType<UnitAddedBattleEvent>().Any());
  }

  [TestCase(TestName = "Event raised by a hook dispatches after the current event and reaches other hooks")]
  public void HookRaisedEventDispatchesAfterCurrentEvent()
  {
    var (session, executor, faction) = MakeSessionWithUnit();
    session.RegisterHook<TurnEndedBattleEvent>(new RaiseProbeOnTurnEndedHook(), HookPhase.After);
    var hook = new CountingHook();
    session.RegisterHook<ProbeBattleEvent>(hook, HookPhase.After);
    StartBattle(session);

    var recorder = new BattleEventRecorder(session);
    var result = executor.Submit(BattleAction.EndFactionTurn(faction)).RequireSingleResult();

    Assert.True(result.Succeeded);
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

  [TestCase(TestName = "A throwing hook fails the action and clears undispatched events")]
  public void ThrowingHookFailsActionAndClearsQueue()
  {
    var (session, executor, faction) = MakeSessionWithUnit();
    session.RegisterHook<TurnEndedBattleEvent>(new RaiseThenThrowOnceOnTurnEndedHook(), HookPhase.After);
    StartBattle(session);

    var recorder = new BattleEventRecorder(session);
    var result = executor.Submit(BattleAction.EndFactionTurn(faction)).RequireSingleResult();

    Assert.False(result.Succeeded);
    Assert.False(recorder.OfType<ProbeBattleEvent>().Any());

    var followUp = executor.Submit(BattleAction.EndFactionTurn(faction)).RequireSingleResult();
    Assert.True(followUp.Succeeded);
    Assert.False(recorder.OfType<ProbeBattleEvent>().Any());
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

  [TestCase(TestName = "Before hooks fire before the broadcast and After hooks after it")]
  public void BeforeFiresPreBroadcastAfterFiresPostBroadcast()
  {
    var (session, executor, faction) = MakeSessionWithUnit();
    bool beforeRan = false;
    bool afterRan = false;
    Option<(bool Before, bool After)> observedAtBroadcast = None;
    session.RegisterHook<TurnEndedBattleEvent>(new MarkingHook(() => beforeRan = true), HookPhase.Before);
    session.RegisterHook<TurnEndedBattleEvent>(new MarkingHook(() => afterRan = true), HookPhase.After);
    session.BattleEventCommitted += battleEvent =>
    {
      if (battleEvent is TurnEndedBattleEvent)
        observedAtBroadcast = Some((beforeRan, afterRan));
    };
    StartBattle(session);

    executor.Submit(BattleAction.EndFactionTurn(faction)).RequireSingleResult();

    Assert.Equal((true, false), observedAtBroadcast.RequireSome());
    Assert.True(afterRan);
  }

  [TestCase(TestName = "A hook returning interrupts outside an executor action throws")]
  public void InterruptsOutsideExecutorActionThrow()
  {
    var (session, _, _) = MakeSessionWithUnit();
    session.RegisterHook<TurnStartedBattleEvent>(
      new InterruptingHook(context => BattleAction.EndFactionTurn(context.Session.ActiveSide)),
      HookPhase.After);

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
      context.Session.GetFactionAliveUnits(faction).First()));
    session.RegisterHook<UnitMovedBattleEvent>(hook, HookPhase.After);

    var unit = session.GetFactionAliveUnits(faction).First();
    executor.Submit(BattleAction.MoveUnit(unit, [session.Board.At(1, 0, 2)]));

    Assert.True(hook.ObservedSourceAction.IsSome);
    Assert.True(hook.ObservedSourceAction.RequireSome() is MoveUnitStep);
  }
}
