using FunProject.Battle;
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

  [TestCase(TestName = "Hook registered for a concrete event type receives only that event")]
  public void HookReceivesOnlyItsConcreteEvent()
  {
    var faction = TestData.MakeFaction("Player");
    using var battle = new BattleFixture(new Vector3I(4, 1, 4), [faction]);
    battle.Spawn(TestData.MakeCombatant("Alpha", faction), new Vector3I(1, 0, 1));
    var turnStartedListener = new RecordingHook();
    var damagedListener = new RecordingHook();
    battle.RegisterHook<TurnStartedBattleEvent>(turnStartedListener);
    battle.RegisterHook<UnitDamagedBattleEvent>(damagedListener);

    battle.Start();

    Assert.Equal(1, turnStartedListener.Received.Count);
    Assert.True(turnStartedListener.Received.AsValueEnumerable().Single() is TurnStartedBattleEvent);
    Assert.Equal(0, damagedListener.Received.Count);
  }

  [TestCase(TestName = "Hook registered for a marker interface receives implementing events")]
  public void HookReceivesMarkerInterfaceEvents()
  {
    var faction = TestData.MakeFaction("Player");
    using var battle = new BattleFixture(new Vector3I(4, 1, 4), [faction]);
    var unitEventsListener = new RecordingHook();
    battle.RegisterHook<IUnitBattleEvent>(unitEventsListener);

    battle.Spawn(TestData.MakeCombatant("Alpha", faction), new Vector3I(1, 0, 1));

    Assert.True(unitEventsListener.Received.AsValueEnumerable().OfType<UnitAddedBattleEvent>().Any());
  }

  [TestCase(TestName = "A hook registered under concrete and interface keys fires once for one event")]
  public void OneHookRegisteredUnderMultipleMatchingKeysFiresOnce()
  {
    var faction = TestData.MakeFaction("Player");
    using var battle = new BattleFixture(new Vector3I(4, 1, 4), [faction]);
    var unit = battle.Spawn(TestData.MakeCombatant("Alpha", faction), new Vector3I(1, 0, 1));
    var hook = new RecordingHook();
    battle.RegisterHook<UnitKilledBattleEvent>(hook);
    battle.RegisterHook<IUnitBattleEvent>(hook);

    battle.ApplyDamage(unit, 999);

    Assert.Equal(1, hook.Received.Count);
  }

  [TestCase(TestName = "Event raised by a hook dispatches after the current event and reaches other hooks")]
  public void HookRaisedEventDispatchesAfterCurrentEvent()
  {
    var faction = TestData.MakeFaction("Player");
    using var battle = new BattleFixture(new Vector3I(4, 1, 4), [faction]);
    battle.Spawn(TestData.MakeCombatant("Alpha", faction), new Vector3I(1, 0, 1));
    battle.RegisterHook<TurnEndedBattleEvent>(new RaiseProbeOnTurnEndedHook());
    var hook = new CountingHook();
    battle.RegisterHook<ProbeBattleEvent>(hook);
    battle.Start();

    battle.ClearEvents();
    battle.Submit(BattleAction.EndFactionTurn(faction));

    battle.Events.EventBefore<TurnEndedBattleEvent, ProbeBattleEvent>();
    battle.Events.EventBefore<ProbeBattleEvent, TurnStartedBattleEvent>();
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
    var faction = TestData.MakeFaction("Player");
    using var battle = new BattleFixture(new Vector3I(4, 1, 4), [faction]);
    battle.Spawn(TestData.MakeCombatant("Alpha", faction), new Vector3I(1, 0, 1));
    battle.RegisterHook<TurnEndedBattleEvent>(new RaiseThenThrowOnceOnTurnEndedHook());
    battle.Start();

    battle.ClearEvents();
    // The simple execution model lets a hook failure surface as the throw it is; the
    // session clears its dispatch queue on the way out.
    Assert.Throws<InvalidOperationException>(
      () => battle.Submit(BattleAction.EndFactionTurn(faction)));
    Assert.False(battle.Events.EventsOf<ProbeBattleEvent>().AsValueEnumerable().Any());

    battle.Submit(BattleAction.EndFactionTurn(faction));
    Assert.False(battle.Events.EventsOf<ProbeBattleEvent>().AsValueEnumerable().Any());
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
    var faction = TestData.MakeFaction("Player");
    using var battle = new BattleFixture(new Vector3I(4, 1, 4), [faction]);
    battle.Spawn(TestData.MakeCombatant("Alpha", faction), new Vector3I(1, 0, 1));
    battle.RegisterHook<TurnStartedBattleEvent>(
      new InterruptingHook(context => BattleAction.EndFactionTurn(context.Session.ActiveSide)));

    // session.StartBattle raises TurnStarted with no executor action in flight. Pre-assign
    // the faction's objective exactly as the started path would — or the no-objective guard
    // throws the same exception type and the test false-passes; then assert on the message
    // to pin the right throw.
    battle.Session.AddObjective(faction, new EliminateAllOpposingForcesObjectiveData().Instantiate());

    InvalidOperationException thrown = null;
    try
    {
      battle.Session.StartBattle();
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
    var faction = TestData.MakeFaction("Player");
    using var battle = new BattleFixture(new Vector3I(4, 1, 4), [faction]);
    var unit = battle.Spawn(TestData.MakeCombatant("Alpha", faction), new Vector3I(1, 0, 1));
    battle.Start();
    var hook = new InterruptingHook(context => BattleAction.PassUnit(
      context.Session.TryGetAlive(context.Session.GetFactionAliveUnits(faction).AsValueEnumerable().First()).RequireSome()));
    battle.RegisterHook<UnitMovedBattleEvent>(hook);

    battle.Submit(BattleAction.MoveUnit(battle.Alive(unit), [battle.At(1, 0, 2)]));

    Assert.True(hook.ObservedSourceAction.IsSome);
    Assert.True(hook.ObservedSourceAction.RequireSome() is MoveUnit);
  }

  [TestCase(TestName = "One-shot hook retires itself by signalling NeedsToUnregister")]
  public void OneShotHookRetiresItselfThroughNeedsToUnregister()
  {
    using var battle = BattleFixture.Solo(new Vector3I(3, 1, 3), new Vector3I(0, 0, 0));

    var hook = new SelfRetiringHook();
    battle.RegisterHook<TurnStartedBattleEvent>(hook);

    battle.AdvanceTurn();
    battle.AdvanceTurn();

    Assert.Equal(1, hook.Evaluations);
  }

  [TestCase(TestName = "A retired one-shot hook is removed from every key it was registered under")]
  public void RetiredOneShotHookIsRemovedFromEveryKey()
  {
    using var battle = BattleFixture.Solo(new Vector3I(3, 1, 3), new Vector3I(0, 0, 0));
    var unit = battle.Unit;

    // Same instance under two keys: retiring after the UnitMoved firing must also clear
    // the TurnStarted registration — the registry owns removal, not the hook.
    var hook = new SelfRetiringHook();
    battle.RegisterHook<UnitMovedBattleEvent>(hook);
    battle.RegisterHook<TurnStartedBattleEvent>(hook);

    battle.Submit(BattleAction.MoveUnit(battle.Alive(unit), [battle.At(1, 0, 0)]));

    battle.AdvanceTurn();
    battle.AdvanceTurn();

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
