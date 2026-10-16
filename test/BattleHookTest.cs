#nullable disable warnings
using FunProject.Battle;
using GdUnit4;
using Godot;
using System;
using System.Collections.Generic;

namespace FunProject.Tests;

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
  public static void HookReceivesOnlyItsConcreteEvent()
  {
    var faction = TestData.MakeFaction("Player");
    var turnStartedListener = new RecordingHook();
    var damagedListener = new RecordingHook();
    var setup = new BattleSetup(
      TestData.MakeOpenBattleMap(new Vector3I(4, 1, 4)),
      [new BattleSideSetup(faction, [new FakeObjectiveData()],
        [new UnitPlacement(new UnitLoadout(TestData.MakeCombatant("Alpha", faction)), new Vector3I(1, 0, 1))])],
      Seed: 7) with
    {
      Systems = [new SetupSystemData { OnRegister = runtime =>
      {
        runtime.RegisterHook<TurnStartedBattleEvent>(turnStartedListener);
        runtime.RegisterHook<UnitDamagedBattleEvent>(damagedListener);
      } }]
    };
    using var battle = new BattleFixture(setup);

    Assert.Equal(1, turnStartedListener.Received.Count);
    Assert.True(turnStartedListener.Received.AsValueEnumerable().Single() is TurnStartedBattleEvent);
    Assert.Equal(0, damagedListener.Received.Count);
  }

  [TestCase(TestName = "A hook registered under the root event tag receives every committed event")]
  public static void RootTagRegistrationReceivesEveryCommittedEvent()
  {
    var faction = TestData.MakeFaction("Player");
    var catchAll = new RecordingHook();
    var setup = new BattleSetup(
      TestData.MakeOpenBattleMap(new Vector3I(4, 1, 4)),
      [new BattleSideSetup(faction, [new FakeObjectiveData()],
        [new UnitPlacement(new UnitLoadout(TestData.MakeCombatant("Alpha", faction)), new Vector3I(1, 0, 1))])],
      Seed: 7) with
    {
      Systems = [new SetupSystemData { OnRegister = runtime =>
        runtime.RegisterHook<IBattleEventTag>(catchAll) }]
    };
    using var battle = new BattleFixture(setup);

    Assert.True(catchAll.Received.AsValueEnumerable().OfType<UnitAddedBattleEvent>().Any());
    Assert.True(catchAll.Received.AsValueEnumerable().OfType<TurnStartedBattleEvent>().Any());

    battle.ClearEvents();
    battle.Submit(BattleAction.EndFactionTurn(faction));

    Assert.True(catchAll.Received.AsValueEnumerable().OfType<TurnEndedBattleEvent>().Any());
  }

  [TestCase(TestName = "Hook firing order follows priority then registration across different event keys")]
  public static void PriorityThenRegistrationOrderHoldsAcrossDifferentEventKeys()
  {
    using var battle = BattleFixture.Solo(new Vector3I(4, 1, 4), new Vector3I(0, 0, 0));
    var log = new List<string>();
    battle.RegisterHook<IUnitBattleEvent>(new OrderedHook("unit-default", log));
    battle.RegisterHook<IPositionedBattleEvent>(new OrderedHook("positioned-low-priority", log), -5);
    battle.RegisterHook<UnitAddedBattleEvent>(new OrderedHook("concrete-default", log));

    battle.Spawn(TestData.MakeCombatant("Reinforcement", battle.PlayerFaction), new Vector3I(1, 0, 1));

    Assert.Equal("positioned-low-priority|unit-default|concrete-default", string.Join('|', log));
  }

  [TestCase(TestName = "A hook registered under concrete and interface keys fires once for one event")]
  public static void OneHookRegisteredUnderMultipleMatchingKeysFiresOnce()
  {
    using var battle = BattleFixture.Solo(new Vector3I(4, 1, 4), new Vector3I(1, 0, 1));
    var unit = battle.Unit;
    var hook = new RecordingHook();
    battle.RegisterHook<UnitKilledBattleEvent>(hook);
    battle.RegisterHook<IUnitBattleEvent>(hook);

    battle.ApplyDamage(unit, 999);

    Assert.Equal(1, hook.Received.Count);
  }

  [TestCase(TestName = "Event raised by a hook dispatches after the current event and reaches other hooks")]
  public static void HookRaisedEventDispatchesAfterCurrentEvent()
  {
    var faction = TestData.MakeFaction("Player");
    var hook = new CountingHook();
    var setup = new BattleSetup(
      TestData.MakeOpenBattleMap(new Vector3I(4, 1, 4)),
      [new BattleSideSetup(faction, [new FakeObjectiveData()],
        [new UnitPlacement(new UnitLoadout(TestData.MakeCombatant("Alpha", faction)), new Vector3I(1, 0, 1))])],
      Seed: 7) with
    {
      Systems =
        [
          new SetupSystemData { OnRegister = runtime =>
            runtime.RegisterHook<TurnEndedBattleEvent>(new RaiseProbeOnTurnEndedHook()) },
          new SetupSystemData { OnRegister = runtime =>
            runtime.RegisterHook<ProbeBattleEvent>(hook) },
        ],
    };
    using var battle = new BattleFixture(setup);

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
  public static void ThrowingHookPropagatesAndClearsQueue()
  {
    var faction = TestData.MakeFaction("Player");
    var setup = new BattleSetup(
      TestData.MakeOpenBattleMap(new Vector3I(4, 1, 4)),
      [new BattleSideSetup(faction, [new FakeObjectiveData()],
        [new UnitPlacement(new UnitLoadout(TestData.MakeCombatant("Alpha", faction)), new Vector3I(1, 0, 1))])],
      Seed: 7) with
    {
      Systems = [new SetupSystemData { OnRegister = runtime =>
        runtime.RegisterHook<TurnEndedBattleEvent>(new RaiseThenThrowOnceOnTurnEndedHook()) }]
    };
    using var battle = new BattleFixture(setup);

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
  public static void InterruptsOutsideExecutorActionThrow()
  {
    using var battle = BattleFixture.Solo(new Vector3I(3, 1, 3), new Vector3I(0, 0, 0));
    var unit = battle.Unit;
    battle.RegisterHook<UnitMovedBattleEvent>(
      new InterruptingHook(context => BattleAction.EndFactionTurn(context.Session.ActiveSide)));

    // Session-direct mutation keeps this at the real no-in-flight boundary without a start
    // operation: the hook sees the movement event outside any executor action window.
    var from = battle.Session.GetUnitPosition(unit).RequireSome();
    var to = battle.Board.At(new Vector3I(1, 0, 0));
    InvalidOperationException thrown = null;
    try
    {
      battle.Session.MoveUnit(unit, from, to);
    }
    catch (InvalidOperationException exception)
    {
      thrown = exception;
    }

    Assert.True(thrown is not null, "Expected the session mutation to throw when a hook returns interrupts outside an executor action.");
    Assert.True(thrown.Message.Contains("interrupt", StringComparison.Ordinal));
  }

  [TestCase(TestName = "Hooks receive the in-flight action as SourceAction during executor dispatches")]
  public static void HooksReceiveSourceActionInsideExecutorDispatch()
  {
    using var battle = BattleFixture.Solo(new Vector3I(4, 1, 4), new Vector3I(1, 0, 1));
    var unit = battle.Unit;
    var hook = new InterruptingHook(context => BattleAction.PassUnit(
      context.Session.TryGetAlive(context.Session.GetFactionAliveUnits(battle.PlayerFaction).AsValueEnumerable().First()).RequireSome()));
    battle.RegisterHook<UnitMovedBattleEvent>(hook);

    battle.Submit(BattleAction.MoveUnit(battle.Alive(unit), [battle.At(1, 0, 2)]));

    Assert.True(hook.ObservedSourceAction.IsSome);
    Assert.True(hook.ObservedSourceAction.RequireSome() is MoveUnit);
  }

  [TestCase(false, TestName = "One-shot hook retires itself by signalling NeedsToUnregister")]
  [TestCase(true, TestName = "A retired one-shot hook is removed from every key it was registered under")]
  public static void OneShotRetiresAcrossRegisteredKeys(bool multipleKeys)
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
