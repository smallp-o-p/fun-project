#nullable disable warnings
using FunProject.Battle;
using FunProject.Weapons;
using GdUnit4;
using Godot;
using System;
using System.Collections.Generic;

[TestSuite]
[RequireGodotRuntime]
public partial class BattleHookTest
{
  private sealed record ProbeBattleEvent : BattleEvent<ProbeBattleEvent>;

  private sealed class RaiseProbeOnTurnEndedHook : BattleHook
  {
    public override IReadOnlyList<BattleAction> OnEvent(HookContext context, BattleEvent battleEvent)
    {
      if (battleEvent is TurnEndedBattleEvent)
        context.Read.RunningSession.IfSome(session => session.RaiseEvents(new ProbeBattleEvent()));

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

  private sealed class OrderedHook : BattleHook
  {
    private readonly string _label;
    private readonly List<string> _log;

    public OrderedHook(string label, List<string> log)
    {
      _label = label;
      _log = log;
    }

    public override IReadOnlyList<BattleAction> OnEvent(HookContext context, BattleEvent battleEvent)
    {
      if (battleEvent is UnitAddedBattleEvent)
        _log.Add(_label);

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
    battle.Spawn(TestData.MakeCombatant("Alpha", faction), new Vector3I(1, 0, 1));
    var unitEventsListener = new RecordingHook();
    battle.RegisterHook<IUnitBattleEvent>(unitEventsListener);
    battle.Start();

    battle.Spawn(TestData.MakeCombatant("Bravo", faction), new Vector3I(2, 0, 2));

    Assert.True(unitEventsListener.Received.AsValueEnumerable().OfType<UnitAddedBattleEvent>().Any());
  }

  [TestCase(TestName = "A hook registered under the root event tag receives every committed event")]
  public void RootTagRegistrationReceivesEveryCommittedEvent()
  {
    var faction = TestData.MakeFaction("Player");
    using var battle = new BattleFixture(new Vector3I(4, 1, 4), [faction]);
    battle.Spawn(TestData.MakeCombatant("Alpha", faction), new Vector3I(1, 0, 1));
    var catchAll = new RecordingHook();
    battle.RegisterHook<BattleEventTag>(catchAll);

    battle.Start();

    // Registration binds after complete preparation: the opening events arrive, the
    // preparation placement stream does not replay.
    Assert.True(catchAll.Received.AsValueEnumerable().OfType<SessionStartedBattleEvent>().Any());
    Assert.True(catchAll.Received.AsValueEnumerable().OfType<TurnStartedBattleEvent>().Any());
    Assert.Equal(0, catchAll.Received.EventsOf<UnitAddedBattleEvent>().Length);

    battle.Spawn(TestData.MakeCombatant("Late", faction), new Vector3I(2, 0, 2));
    Assert.True(catchAll.Received.AsValueEnumerable().OfType<UnitAddedBattleEvent>().Any());

    battle.ClearEvents();
    battle.Submit(BattleAction.EndFactionTurn(faction));

    Assert.True(catchAll.Received.AsValueEnumerable().OfType<TurnEndedBattleEvent>().Any());
  }

  [TestCase(TestName = "Hook firing order follows priority then registration across different event keys")]
  public void PriorityThenRegistrationOrderHoldsAcrossDifferentEventKeys()
  {
    var faction = TestData.MakeFaction("Player");
    using var battle = new BattleFixture(new Vector3I(4, 1, 4), [faction]);
    battle.Spawn(TestData.MakeCombatant("Alpha", faction), new Vector3I(1, 0, 1));
    var log = new List<string>();
    battle.RegisterHook<IUnitBattleEvent>(new OrderedHook("unit-default", log));
    battle.RegisterHook<IPositionedBattleEvent>(new OrderedHook("positioned-low-priority", log), -5);
    battle.RegisterHook<UnitAddedBattleEvent>(new OrderedHook("concrete-default", log));
    battle.Start();

    battle.Spawn(TestData.MakeCombatant("Bravo", faction), new Vector3I(2, 0, 2));

    Assert.Equal("positioned-low-priority|unit-default|concrete-default", string.Join("|", log));
  }

  [TestCase(TestName = "A hook registered under concrete and interface keys fires once for one event")]
  public void OneHookRegisteredUnderMultipleMatchingKeysFiresOnce()
  {
    var faction = TestData.MakeFaction("Player");
    using var battle = new BattleFixture(new Vector3I(4, 1, 4), [faction]);
    var unit = battle.Spawn(TestData.MakeCombatant("Alpha", faction), new Vector3I(1, 0, 1));
    battle.Start();
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
      context.Read.RunningSession.IfSome(session => session.RaiseEvents(new ProbeBattleEvent()));
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

  [TestCase(TestName = "Interrupts returned during the settlement end event are collected and discarded")]
  public void EndEventInterruptsAreDiscarded()
  {
    using var battle = BattleFixture.Duel();
    battle.ApplyDamage(battle.PlayerUnit, 20, DamageKind.Stun);
    battle.ApplyDamage(battle.EnemyUnit, 20, DamageKind.Stun);
    battle.RegisterHook<SessionEndedBattleEvent>(new InterruptingHook(context =>
      BattleAction.PassUnit(context.Read.State.TryGetAlive(battle.EnemyUnit).RequireSome())));

    // The second knockout leaves no conscious forces; the turn-end settlement broadcasts
    // SessionEnded with no in-flight action, and the hook's interrupt is dropped, not run.
    battle.EndFactionTurn(battle.PlayerFaction);

    Assert.True(battle.Query(new GetCompletedBattleQuery()).RequireSome().Outcome == BattleOutcome.Draw);
    Assert.Equal(0, battle.Events.EventsOf<UnitActivationEndedBattleEvent>().AsValueEnumerable().Count());
  }

  [TestCase(TestName = "Hooks receive the in-flight action as SourceAction during executor dispatches")]
  public void HooksReceiveSourceActionInsideExecutorDispatch()
  {
    var faction = TestData.MakeFaction("Player");
    using var battle = new BattleFixture(new Vector3I(4, 1, 4), [faction]);
    var unit = battle.Spawn(TestData.MakeCombatant("Alpha", faction), new Vector3I(1, 0, 1));
    battle.Start();
    var hook = new InterruptingHook(context => BattleAction.PassUnit(
      context.Read.State.TryGetAlive(context.Read.State.GetFactionAliveUnits(faction).AsValueEnumerable().First()).RequireSome()));
    battle.RegisterHook<UnitMovedBattleEvent>(hook);

    battle.Submit(BattleAction.MoveUnit(battle.Alive(unit), [battle.At(1, 0, 2)]));

    Assert.True(hook.ObservedSourceAction.IsSome);
    Assert.True(hook.ObservedSourceAction.RequireSome() is MoveUnit);
  }

  [TestCase(false, TestName = "One-shot hook retires itself by signalling NeedsToUnregister")]
  [TestCase(true, TestName = "A retired one-shot hook is removed from every key it was registered under")]
  public void OneShotRetiresAcrossRegisteredKeys(bool multipleKeys)
  {
    using var battle = BattleFixture.Solo(new Vector3I(3, 1, 3), new Vector3I(0, 0, 0));

    var hook = new SelfRetiringHook();
    battle.RegisterHook<TurnStartedBattleEvent>(hook);
    if (multipleKeys)
    {
      // Same instance under two keys: retiring after the UnitMoved firing must also clear
      // the TurnStarted registration — the registry owns removal, not the hook.
      battle.RegisterHook<UnitMovedBattleEvent>(hook);
      battle.Submit(BattleAction.MoveUnit(battle.Alive(battle.Unit), [battle.At(1, 0, 0)]));
    }

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
